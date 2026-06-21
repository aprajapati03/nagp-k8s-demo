using Npgsql;
using System.Text.Json;
using App.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

// Read DB Configuration from Environment Variables
var host = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
var port = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
var dbName = Environment.GetEnvironmentVariable("DB_NAME") ?? "postgres";
var dbUser = Environment.GetEnvironmentVariable("DB_USER") ?? "postgres";
var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "postgres";

// Npgsql connection string with connection pooling enabled (Pooling=true) and pool sizes defined
var connString = $"Host={host};Port={port};Database={dbName};Username={dbUser};Password={dbPassword};Pooling=true;MinPoolSize=2;MaxPoolSize=10;Timeout=15;CommandTimeout=30;";

Console.WriteLine($"[STARTUP] Starting Web API on port 8080. Target DB: {host}:{port}/{dbName} as User: {dbUser}");

builder.Services.AddSingleton(sp => NpgsqlDataSource.Create(connString));
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

InitDatabase(connString);

app.MapGet("/api/records", async (NpgsqlDataSource dataSource) =>
{
    Console.WriteLine($"[GET /api/records] Fetching all records from database. Served by: {Environment.MachineName}");
    var records = new List<object>();
    try
    {
        using var conn = await dataSource.OpenConnectionAsync();
        using var cmd = new NpgsqlCommand("SELECT id, name, role, department, salary, date_joined FROM employees ORDER BY id ASC;", conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(new
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Role = reader.GetString(2),
                Department = reader.GetString(3),
                Salary = reader.GetInt32(4),
                DateJoined = reader.GetDateTime(5).ToString("yyyy-MM-dd")
            });
        }
        Console.WriteLine($"[GET /api/records] Successfully fetched {records.Count} records.");
        return Results.Ok(new { status = "success", count = records.Count, data = records });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[API ERROR] Error querying records: {ex.Message}");
        return Results.Json(new { status = "error", message = "Database query failed", detail = ex.Message }, statusCode: 500);
    }
});

app.MapPost("/api/records", async (NpgsqlDataSource dataSource, EmployeeInput input) =>
{
    Console.WriteLine($"[POST /api/records] Received request to add new employee: {input.Name}, Role: {input.Role}, Dept: {input.Department}, Salary: {input.Salary}");
    if (string.IsNullOrWhiteSpace(input.Name) || 
        string.IsNullOrWhiteSpace(input.Role) || 
        string.IsNullOrWhiteSpace(input.Department) || 
        input.Salary <= 0)
    {
        Console.WriteLine("[POST /api/records] Validation failed: missing fields or invalid salary.");
        return Results.BadRequest(new { status = "error", message = "Invalid input data. Please fill all fields." });
    }

    try
    {
        using var conn = await dataSource.OpenConnectionAsync();
        using var cmd = new NpgsqlCommand(
            "INSERT INTO employees (name, role, department, salary, date_joined) VALUES (@name, @role, @department, @salary, @date_joined) RETURNING id;", 
            conn);
        
        cmd.Parameters.AddWithValue("name", input.Name);
        cmd.Parameters.AddWithValue("role", input.Role);
        cmd.Parameters.AddWithValue("department", input.Department);
        cmd.Parameters.AddWithValue("salary", input.Salary);
        
        if (!DateTime.TryParse(input.DateJoined, out var dateJoined))
        {
            dateJoined = DateTime.UtcNow.Date;
        }
        cmd.Parameters.AddWithValue("date_joined", dateJoined);

        var newId = (int?)await cmd.ExecuteScalarAsync();
        Console.WriteLine($"[POST /api/records] Successfully inserted record into database with generated ID: {newId}");
        
        return Results.Created($"/api/records/{newId}", new { 
            status = "success", 
            message = "Record added successfully", 
            data = new {
                Id = newId,
                Name = input.Name,
                Role = input.Role,
                Department = input.Department,
                Salary = input.Salary,
                DateJoined = dateJoined.ToString("yyyy-MM-dd")
            }
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[API ERROR] Error inserting record: {ex.Message}");
        return Results.Json(new { status = "error", message = "Database insert failed", detail = ex.Message }, statusCode: 500);
    }
});

app.MapGet("/healthz", async (NpgsqlDataSource dataSource) =>
{
    try
    {
        using var conn = await dataSource.OpenConnectionAsync();
        using var cmd = new NpgsqlCommand("SELECT 1;", conn);
        await cmd.ExecuteScalarAsync();
        return Results.Ok(new { status = "healthy", dbConnection = "success", timestamp = DateTime.UtcNow });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[HEALTH ERROR] Health check failed: {ex.Message}");
        return Results.Json(new { status = "unhealthy", detail = ex.Message, timestamp = DateTime.UtcNow }, statusCode: 500);
    }
});

app.MapRazorComponents<AppRoot>()
    .AddInteractiveServerRenderMode();

app.Run("http://0.0.0.0:8080");

void InitDatabase(string connectionString)
{
    int retries = 10;
    int delayMs = 3000;
    for (int i = 1; i <= retries; i++)
    {
        try
        {
            Console.WriteLine($"[DB INIT] Connecting to database (Attempt {i}/{retries})...");
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            Console.WriteLine("[DB INIT] Connected successfully. Checking table structure...");
            
            // Create Table if not exists
            using (var cmd = new NpgsqlCommand(@"
                CREATE TABLE IF NOT EXISTS employees (
                    id SERIAL PRIMARY KEY,
                    name VARCHAR(100) NOT NULL,
                    role VARCHAR(100) NOT NULL,
                    department VARCHAR(100) NOT NULL,
                    salary INT NOT NULL,
                    date_joined DATE NOT NULL
                );", conn))
            {
                cmd.ExecuteNonQuery();
            }

            using (var transaction = conn.BeginTransaction())
            {
                using (var lockCmd = new NpgsqlCommand("LOCK TABLE employees IN EXCLUSIVE MODE;", conn, transaction))
                {
                    lockCmd.ExecuteNonQuery();
                }

                long count = 0;
                using (var countCmd = new NpgsqlCommand("SELECT COUNT(*) FROM employees;", conn, transaction))
                {
                    var result = countCmd.ExecuteScalar();
                    if (result != null)
                    {
                        count = (long)result;
                    }
                }

                if (count == 0)
                {
                    Console.WriteLine("[DB INIT] Table is empty. Seeding initial records...");
                    using (var seedCmd = new NpgsqlCommand(@"
                        INSERT INTO employees (name, role, department, salary, date_joined) VALUES
                        ('Dona Singh', 'Software Engineer', 'Engineering', 95000, '2023-01-15'),
                        ('Nancy Patel', 'Product Manager', 'Product', 105000, '2022-05-20'),
                        ('Jiya Sharma', 'UX Designer', 'Design', 85000, '2023-08-10'),
                        ('Diana Patel', 'DevOps Engineer', 'Engineering', 110000, '2021-11-01'),
                        ('Neha Mehta', 'Security Analyst', 'Security', 98000, '2024-02-14'),
                        ('Parth Agrawal', 'HR Specialist', 'HR', 75000, '2022-09-05'),
                        ('Rutu Raj', 'Data Scientist', 'Data Science', 115000, '2023-03-22');", conn, transaction))
                    {
                        seedCmd.ExecuteNonQuery();
                    }
                    Console.WriteLine("[DB INIT] Seeding completed successfully.");
                }
                else
                {
                    Console.WriteLine($"[DB INIT] Database already contains {count} records. Skipping seeding.");
                }

                transaction.Commit();
            }
            
            break; 
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB INIT ERROR] Attempt {i} failed: {ex.Message}");
            if (i == retries)
            {
                Console.WriteLine("[DB INIT FATAL] Max retries reached. Database initialization failed.");
            }
            else
            {
                Thread.Sleep(delayMs);
            }
        }
    }
}

public record EmployeeInput(string Name, string Role, string Department, int Salary, string? DateJoined);
