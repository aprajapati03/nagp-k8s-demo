# Kubernetes Multi-Tier Architecture (.NET + PostgreSQL)

This repository contains the source code and Kubernetes manifests for a containerized, self-healing, auto-scaling, and cost-optimized multi-tier application.


## 🛠 Tech Stack
- **Service API**: `.NET 8.0 Web API` 
- **Database**: `PostgreSQL 15`
- **Frontend Dashboard**: `Blazor` 
- **Orchestration**: `Kubernetes` 

---

## 🚀 Deployment Steps

### 1. Build and Push the Docker Image
Navigate to the application source directory and build the Docker container:
```bash
# Go to the application directory
cd src/App/

# Build the Docker image
docker build -t USERNAME/k8s-demo-app:latest .

# Log in to Docker Hub 
docker login

# Push the image to Docker Hub
docker push USERNAME/k8s-demo-app:latest
```

### 2. Configure Kubernetes manifests
Update the image field in `k8s/app/deployment.yaml` with your Docker Hub username:
```yaml
spec:
  containers:
    - name: web-api
      image: YOUR_DOCKER_USERNAME/k8s-demo-app:latest
```

### 3. Deploy to Kubernetes
Deploy the manifests by folders:
```bash
# Create the namespace first (if not already existing)
kubectl apply -f k8s/namespace.yaml

# Create the secret
kubectl apply -f k8s/secret.yaml

# Create the configmap
kubectl apply -f k8s/configmap.yaml

# Apply the Database (StatefulSet & Service)
kubectl apply -f k8s/db/ -n nagp-k8s-demo

# Apply the Application (Deployment, Service, Ingress, HPA)
kubectl apply -f k8s/app/ -n nagp-k8s-demo
```
