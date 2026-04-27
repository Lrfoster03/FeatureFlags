# 🚩 Feature Flags Dashboard

A simple full-stack feature flag management dashboard built with **Blazor Server**, **EF Core**, and **SQLite**.

![alt text](image.png)

This application allows you to:

* Create and manage feature flags
* Toggle flags on/off
* Persist flags across sessions
* Expose a REST API for consuming feature flags in other applications

---

## ✨ Features

* 🎛️ Interactive UI with expandable "pill" components
* 💾 Persistent storage using SQLite
* 🔄 Real-time updates with Blazor Server
* 🚫 Duplicate flag name prevention
* 🔌 REST API endpoint for external consumers
* ⚡ Lightweight and easy to run locally

---

## 🧱 Tech Stack

* **Frontend / Backend**: Blazor Server (.NET 10)
* **UI Components**: Blazor Bootstrap
* **ORM**: Entity Framework Core
* **Database**: SQLite
* **API**: ASP.NET Core Minimal APIs

---

## 🚀 Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/your-username/feature-flags.git
cd feature-flags
```

### 2. Install dependencies

```bash
dotnet restore
brew install --cask docker
```

### 3. Apply database migrations

```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### 4. Run the app

```bash
./scripts/docker-up.sh
```

Open your browser at:

```
http://localhost:<selected-port>
```

### 5. Run with Docker

Build and run the container directly:

```bash
docker build -t featureflags .
docker run --rm -p 8080:8080 -v featureflags-data:/data featureflags
```

Or use Docker Compose:

```bash
./scripts/docker-up.sh
```

Then open:

```
http://localhost:<selected-port>
```

Notes:

* The container stores the SQLite database at `/data/featureflags.db`
* `docker-compose.yml` mounts `/data` as a named volume so data survives container restarts
* The app still applies EF Core migrations automatically on startup
* `scripts/docker-up.sh` picks the first available host port from `8080` to `8090`
* You can override the search range: `./scripts/docker-up.sh 8085 8100`
* Running `docker compose up --build` directly still defaults to host port `8080`; set `HOST_PORT` yourself if you want to pick a different one

---

## 📡 API Usage

### Get all feature flags

```bash
curl --location 'http://localhost:<selected-port>/api/featureflags' \
  --header 'user: <userID>'
```

### Example Response

```json
{
  "featureFlags": {
    "NewUI": true,
    "BetaCheckout": false
  }
}
```

---

## ⚠️ Validation

* Feature flag names are **unique**
* Duplicate names are prevented via:

  * Database unique index
  * Application-level validation
  * Graceful UI error handling

---

## 📁 Project Structure

```
/Components
  /Shared
    Pill.razor          # Feature flag UI component
/Data
  FeatureFlagDbContext.cs
/Models
  FeatureFlag.cs
Program.cs              # App + API setup
```

---

## 🛠️ Development Notes

* SQLite database file: `featureflags.db`
* Migrations stored in `/Migrations`
* Uses `DbContext` injection for data access
* UI changes auto-save to database
* Override the runtime DB path with `ConnectionStrings__FeatureFlags`

---

## 🤝 Contributing

Feel free to fork the repo and submit PRs!

---

## 📄 License

MIT License

---
