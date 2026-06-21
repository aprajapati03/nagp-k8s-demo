# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["App.csproj", "./"]
RUN dotnet restore "App.csproj"

# Copy the remaining source files and compile
COPY . .
RUN dotnet publish "App.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Production Runtime Environment
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080

# Copy published artifacts from build stage
COPY --from=build /app/publish .

# Use built-in non-root 'app' user for security compliance and FinOps practices
USER app

ENTRYPOINT ["dotnet", "App.dll"]
