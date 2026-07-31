# RunSync

> Sync your Strava activities to your personalized half marathon training plan.

RunSync is a full-stack web application that authenticates with Strava via OAuth 2.0, syncs your running activities, and overlays them on a structured 12-week half marathon training plan — showing planned vs. actual mileage, pace, and heart rate for every workout.

**Bring your own Strava app.** A free Strava API application has an athlete capacity of 1 ("Single Player Mode"), so a single shared application would cap RunSync at one user. Instead, each user registers their own free Strava application and saves its credentials in RunSync — every user is the only athlete on their own app. Client secrets are encrypted with AES-256-GCM before storage and are never returned by the API. This also means Strava rate limits apply per user rather than being shared.

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
- **Per-user Strava apps** — each user supplies their own API credentials, so there's no shared athlete cap
- **Encrypted secrets at rest** — AES-256-GCM, per-user AAD binding, decrypt-only key rotation
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

Generate an encryption key for the per-user Strava client secrets:

```bash
openssl rand -base64 32
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
    "RedirectUri": "http://localhost:5000/api/strava/callback"
  },
  "Encryption": {
    "MasterKey": "base64-32-bytes-from-openssl-rand",
    "PreviousKeys": []
  },
  "Cors": {
    "AllowedOrigin": "http://localhost:5173"
  },
  "TrainingPlan": {
    "StartDate": "2026-02-02"
  }
}
```

There is no `Strava:ClientId` or `Strava:ClientSecret` — those are per user now, entered in the
app's settings screen and stored encrypted. See [Per-user Strava credentials](#per-user-strava-credentials).

```bash
# Create the database schema
dotnet ef database update

# Start the API (Swagger UI at http://localhost:5000)
dotnet run
```

### Frontend Setup

```bash
cd frontend

# Create local env file (see .env.example)
echo "VITE_API_BASE_URL=http://localhost:5000" > .env.local

npm install
npm run dev        # http://localhost:5173
```

Other scripts: `npm run build` (typecheck + production bundle into `dist/`),
`npm run typecheck`, `npm run preview`.

### First run

1. Open http://localhost:5173 and create an account
2. You'll land in **Settings**, because a new account has no Strava application yet
3. Follow the on-screen steps, or send someone the full walkthrough in
   [docs/CONNECTING_STRAVA.md](docs/CONNECTING_STRAVA.md)
4. Save the credentials, then **Connect Strava** → **Sync Now**

### Environment Variables

| Variable               | Where                         | Description                                    |
|------------------------|-------------------------------|------------------------------------------------|
| `ConnectionStrings__DefaultConnection` | appsettings / AWS EB env | MySQL connection string |
| `Jwt__Key`             | appsettings / AWS EB env      | HMAC-SHA256 signing key (min 32 chars)         |
| `Jwt__Issuer`          | appsettings / AWS EB env      | JWT issuer claim (e.g. `RunSync`)              |
| `Jwt__Audience`        | appsettings / AWS EB env      | JWT audience claim (e.g. `RunSync`)            |
| `Strava__RedirectUri`  | appsettings / AWS EB env      | RunSync's own callback URL, shared by all users |
| `Encryption__MasterKey` | appsettings / AWS EB env     | Base64 32 bytes. Encrypts users' Strava secrets |
| `Encryption__PreviousKeys__0` | AWS EB env (optional)  | Retired key, decrypt-only, kept during rotation |
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
- `runsync/Encryption__MasterKey`
- `runsync/ConnectionStrings__DefaultConnection`

Reference them in your EB environment properties using the `resolve:ssm:` prefix or load them programmatically in `Program.cs`.

> **Losing `Encryption__MasterKey` is unrecoverable.** Every stored Strava client secret is
> encrypted under it; without it, every user has to re-enter their credentials. Back it up
> somewhere separate from the database.

---

## Per-user Strava credentials

### Why

A free Strava API application starts in **Single Player Mode** with an athlete capacity of 1.
Self-upgrading in the Strava dashboard raises that to 10 — still a hard ceiling, and anything
beyond it requires app review. RunSync sidesteps the ceiling entirely: each user registers their
own free application and is the sole athlete on it.

### What the user does

1. Create an application at [strava.com/settings/api](https://www.strava.com/settings/api)
2. Set **Authorization Callback Domain** to RunSync's API host (the settings screen shows the
   exact value with a copy button — it's the host of `Strava__RedirectUri`, no scheme or path)
3. Paste the Client ID and Client Secret into RunSync's settings screen

The in-app Settings screen walks through this. For a fuller version to send to someone —
including troubleshooting for the errors people actually hit — see
**[docs/CONNECTING_STRAVA.md](docs/CONNECTING_STRAVA.md)**.

Every user's application points at the same RunSync callback URL; the signed OAuth `state`
parameter is what identifies which user — and therefore which application — a callback belongs to.

### How the secret is protected

| Concern | Handling |
|---------|----------|
| Algorithm | AES-256-GCM (authenticated — tampering is detected, not silently decrypted) |
| Nonce | Fresh 12 random bytes per encryption, never reused or derived |
| Stored format | `v1.` + base64(nonce ‖ tag ‖ ciphertext) |
| Row binding | The user id is bound in as AAD, so a ciphertext moved to another user's row fails to decrypt |
| Readback | No endpoint returns the secret; the UI shows a masked placeholder and requires a fresh paste |
| Logging | Only the fact of a save is logged. Strava error bodies are logged but never returned to clients |
| Key rotation | `Encryption__MasterKey` encrypts; `Encryption__PreviousKeys` decrypt only |
| Startup | An invalid or missing master key fails the app at boot, not at first use |

ASP.NET Core Data Protection was deliberately *not* used: its default key ring lives on the local
file system, which Elastic Beanstalk wipes on redeploy and does not share between instances —
every stored secret would become permanently undecryptable after a deploy.

### Rotating the master key

1. Add the current key to `Encryption__PreviousKeys__0`
2. Set `Encryption__MasterKey` to a newly generated key
3. Deploy — existing secrets still decrypt under the previous key, new writes use the new one
4. Once every user has re-saved their credentials, remove the previous key

Note that step 4 currently requires user action; there is no bulk re-encryption job yet.

---

## API Reference

All authenticated endpoints require `Authorization: Bearer <jwt>` header.

| Method   | Path                              | Auth | Description                                         |
|----------|-----------------------------------|------|-----------------------------------------------------|
| `POST`   | `/api/auth/register`              | No   | Create account. Returns JWT.                        |
| `POST`   | `/api/auth/login`                 | No   | Login. Returns JWT.                                 |
| `GET`    | `/api/strava/credentials`         | Yes  | Strava app setup status + callback domain. No secret |
| `PUT`    | `/api/strava/credentials`         | Yes  | Save own Client ID + Secret (encrypted on write)    |
| `DELETE` | `/api/strava/credentials`         | Yes  | Remove credentials and any tokens they issued       |
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

StravaAppCredentials              -- the user's OWN Strava API application
  Id                     INT PK AUTO_INCREMENT
  UserId                 INT FK → Users.Id (CASCADE DELETE, UNIQUE)
  ClientId               VARCHAR(64) NOT NULL   -- public; appears in the OAuth URL
  ClientSecretEncrypted  VARCHAR(512) NOT NULL  -- AES-256-GCM, "v1.<base64>" — never plaintext
  CreatedAt              DATETIME NOT NULL
  UpdatedAt              DATETIME NOT NULL

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
- `SecretProtectorTests` — round-trip, nonce uniqueness, tamper detection, cross-user AAD rejection, key rotation
- `StravaCredentialServiceTests` — encryption at rest, credential lifecycle, stale-token cleanup
- `StravaServiceTests` — per-user OAuth URLs, token refresh, Strava error translation, CSRF state validation
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
