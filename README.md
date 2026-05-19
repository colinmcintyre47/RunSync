# RunSync

> Sync your Strava activities to your personalized half marathon training plan.

RunSync is a full-stack web application that authenticates with Strava via OAuth 2.0, syncs your running activities, and overlays them on a structured 12-week half marathon training plan — showing planned vs. actual mileage, pace, and heart rate for every workout.

---

## Tech Stack

| Layer       | Technology                                      |
|-------------|-------------------------------------------------|
| Frontend    | React 18 + TypeScript, Vite, Tailwind CSS       |
| Backend     | ASP.NET Core 8 Web API, C#                      |
| ORM         | Entity Framework Core 8 (Pomelo MySQL provider) |
| Database    | MySQL 8 (AWS RDS in production)                 |
| Auth        | JWT Bearer tokens + BCrypt password hashing     |
| Strava      | OAuth 2.0 + Strava REST API v3                  |
| Hosting     | AWS Elastic Beanstalk (API) + Netlify (frontend)|
| Secrets     | AWS Secrets Manager                             |
| Testing     | xUnit + Moq + FluentAssertions                  |

---

## Architecture Overview

```
┌─────────────────────┐         ┌──────────────────────────────────────────┐
│   React Frontend    │ HTTPS   │           ASP.NET Core 8 API              │
│   (Netlify)         │────────▶│                                           │
│                     │◀────────│  Controllers  →  Services  →  DbContext   │
│  useStravaActivities│  JSON   │  (HTTP layer)    (logic)      (EF Core)   │
│  TrainingDay cards  │         │                                           │
│  ActivityBadge      │         │           MySQL 8 (AWS RDS)               │
└─────────────────────┘         └──────────────────────────────────────────┘
          │                                       │
          │ OAuth redirect                        │ REST API calls
          ▼                                       ▼
┌─────────────────────┐         ┌──────────────────────────────────────────┐
│   Strava OAuth      │         │         Strava API v3                    │
│   (user approves)   │         │   /athlete/activities (paginated)        │
└─────────────────────┘         └──────────────────────────────────────────┘
```

**3-Layer Architecture (backend):**
- **Controllers** — HTTP concerns only (routing, status codes, request parsing)
- **Services** — all business logic (token refresh, activity matching, unit conversion)
- **Data** — EF Core DbContext, no raw SQL

---

## Features

- **Strava OAuth 2.0** — connect and disconnect your Strava account
- **Activity Sync** — paginated fetch of all runs, upserted into local cache
- **Automatic Token Refresh** — expired Strava tokens are refreshed transparently
- **Training Plan Matching** — each run matched to the corresponding plan day by local date
- **Planned vs. Actual** — see target miles alongside your real distance, pace, and HR
- **CSRF Protection** — OAuth state param signed with HMAC-SHA256
- **Multi-user Ready** — JWT-based auth, all data scoped by userId
- **Global Error Handling** — structured JSON errors, no stack traces to clients

---

## Local Development Setup

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/)
- MySQL 8 (local instance or Docker: `docker run -p 3306:3306 -e MYSQL_ROOT_PASSWORD=dev mysql:8`)
- A [Strava API application](https://www.strava.com/settings/api) (free to create)

### Backend Setup

```bash
cd backend/RunSync.Api

# Create local secrets file (git-ignored)
cp appsettings.json appsettings.Development.json
```

Edit `appsettings.Development.json` and fill in all values:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=runsync;User=root;Password=dev;"
  },
  "Jwt": {
    "Key": "your-secret-key-at-least-32-characters-long",
    "Issuer": "RunSync",
    "Audience": "RunSync",
    "ExpiryMinutes": 60
  },
  "Strava": {
    "ClientId": "your-strava-client-id",
    "ClientSecret": "your-strava-client-secret",
    "RedirectUri": "http://localhost:5000/api/strava/callback"
  },
  "Cors": {
    "AllowedOrigin": "http://localhost:5173"
  },
  "TrainingPlan": {
    "StartDate": "2026-02-02"
  }
}
```

```bash
# Create the database schema
dotnet ef database update

# Start the API (Swagger UI at http://localhost:5000)
dotnet run
```

### Frontend Setup

```bash
cd frontend

# Create local env file
echo "VITE_API_BASE_URL=http://localhost:5000" > .env.local

npm install
npm run dev   # http://localhost:5173
```

### Environment Variables

| Variable               | Where                         | Description                                    |
|------------------------|-------------------------------|------------------------------------------------|
| `ConnectionStrings__DefaultConnection` | appsettings / AWS EB env | MySQL connection string |
| `Jwt__Key`             | appsettings / AWS EB env      | HMAC-SHA256 signing key (min 32 chars)         |
| `Jwt__Issuer`          | appsettings / AWS EB env      | JWT issuer claim (e.g. `RunSync`)              |
| `Jwt__Audience`        | appsettings / AWS EB env      | JWT audience claim (e.g. `RunSync`)            |
| `Strava__ClientId`     | appsettings / AWS EB env      | From your Strava API application               |
| `Strava__ClientSecret` | appsettings / AWS EB env      | From your Strava API application               |
| `Strava__RedirectUri`  | appsettings / AWS EB env      | Must match exactly what's set in Strava app    |
| `Cors__AllowedOrigin`  | appsettings / AWS EB env      | Your Netlify site URL (no trailing slash)      |
| `TrainingPlan__StartDate` | appsettings               | ISO date: Monday of Week 1 (e.g. `2026-02-02`)|
| `VITE_API_BASE_URL`    | `.env.local` / Netlify        | Full URL to the backend API                    |

---

## AWS Deployment

### Backend: Elastic Beanstalk

```bash
# Build the release artifact
dotnet publish -c Release -o ./publish

# Package for Elastic Beanstalk
cd publish && zip -r ../runsync-api.zip .

# Deploy via EB CLI or upload in AWS Console
eb deploy
```

Set all environment variables under **Configuration → Software → Environment properties** in the EB console. AWS Secrets Manager values can be pulled at startup using the `AWSSDK.SecretsManager` package.

### Database: RDS MySQL

1. Create an RDS MySQL 8 instance in the same VPC as your Elastic Beanstalk environment
2. Add the EB security group as an inbound rule on port 3306
3. Set `ConnectionStrings__DefaultConnection` in EB environment properties
4. Run migrations from a bastion host or CI pipeline:
   ```bash
   dotnet ef database update --connection "your-rds-connection-string"
   ```

### Secrets: AWS Secrets Manager

Store sensitive values in Secrets Manager as individual string secrets:
- `runsync/Jwt__Key`
- `runsync/Strava__ClientSecret`
- `runsync/ConnectionStrings__DefaultConnection`

Reference them in your EB environment properties using the `resolve:ssm:` prefix or load them programmatically in `Program.cs`.

---

## API Reference

All authenticated endpoints require `Authorization: Bearer <jwt>` header.

| Method   | Path                              | Auth | Description                                         |
|----------|-----------------------------------|------|-----------------------------------------------------|
| `POST`   | `/api/auth/register`              | No   | Create account. Returns JWT.                        |
| `POST`   | `/api/auth/login`                 | No   | Login. Returns JWT.                                 |
| `GET`    | `/api/strava/authorize`           | Yes  | Returns Strava OAuth URL for frontend to redirect to|
| `GET`    | `/api/strava/callback`            | No   | Strava OAuth callback — exchanges code for tokens   |
| `POST`   | `/api/strava/sync`                | Yes  | Manually trigger activity sync from Strava          |
| `DELETE` | `/api/strava/disconnect`          | Yes  | Remove stored Strava tokens                         |
| `GET`    | `/api/activities/training-plan`   | Yes  | Returns 84-day plan with matched Strava activities  |
| `GET`    | `/api/activities/sync-status`     | Yes  | Returns last sync time, activity count, connected?  |

Swagger UI is available at the API root (`/`) in development.

---

## Database Schema

```
Users
  Id            INT PK AUTO_INCREMENT
  Email         VARCHAR(256) UNIQUE NOT NULL
  PasswordHash  TEXT NOT NULL           -- BCrypt hash, work factor 12
  DisplayName   VARCHAR(64) NOT NULL
  CreatedAt     DATETIME NOT NULL

StravaTokens
  Id               INT PK AUTO_INCREMENT
  UserId           INT FK → Users.Id (CASCADE DELETE)
  AccessToken      TEXT NOT NULL
  RefreshToken     TEXT NOT NULL
  ExpiresAt        BIGINT NOT NULL      -- Unix timestamp
  StravaAthleteId  INT NOT NULL
  LastSyncedAt     DATETIME NOT NULL

StravaActivities
  Id                  BIGINT PK        -- Strava's own activity ID (no auto-generate)
  UserId              INT FK → Users.Id (CASCADE DELETE)
  Name                TEXT NOT NULL
  Type                VARCHAR(32) NOT NULL
  DistanceMeters      FLOAT NOT NULL
  MovingTimeSeconds   INT NOT NULL
  AverageHeartrate    FLOAT NOT NULL
  AverageSpeed        FLOAT NOT NULL
  TotalElevationGain  FLOAT NOT NULL
  StartDateUtc        DATETIME NOT NULL  (indexed)
  StartDateLocal      DATETIME NOT NULL  (indexed with UserId)
  IsManualEntry       BOOL NOT NULL
```

---

## Running Tests

```bash
cd backend
dotnet test

# With coverage report
dotnet test --collect:"XPlat Code Coverage"
```

Test coverage areas:
- `TokenServiceTests` — JWT claim generation, expiry, userId extraction
- `ActivityServiceTests` — plan matching, miles conversion, pace formatting, sync status
- `StravaServiceTests` — token refresh, activity upsert, CSRF state validation
- `ActivitiesControllerTests` — HTTP response codes, service delegation, userId forwarding

---

## Future Improvements

- **Custom training plans** — store plans in a `TrainingPlans` DB table instead of hardcoding
- **Webhook integration** — receive real-time push notifications from Strava instead of manual sync
- **Multiple sports** — extend to full triathlon training (cycling, swimming)
- **Race goal calculator** — input a target finish time, auto-adjust pace zones
- **Progressive Web App** — offline support and install-to-homescreen for mobile runners
- **Email reminders** — daily workout notification the morning of each training day
- **Social features** — share weekly summaries or compare plans with a training partner
