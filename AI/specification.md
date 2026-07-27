# Foosball Manager - Specification

**Project Name**: `Fotbalek` (solution and project)

## 1. Description
An application for managing foosball games. A team represents a tournament where players compete against each other by comparing their statistics.

**Scope**: This is a Proof of Concept (PoC) for approximately 10 users. Security and scalability features are intentionally minimal. No need for over-engineered methods and classes.

---

## 2. Data Model

### 2.1 Team
| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | PK, auto-increment | |
| Name | string(100) | required | Display name (does not need to be unique) |
| CodeName | string(50) | required, unique, lowercase, alphanumeric + hyphens, indexed | URL-safe slug selected by user |
| PasswordHash | string(256) | required | BCrypt hashed password |
| CreatedAt | DateTimeOffset | required, default: UtcNow | |

### 2.2 Player
| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | PK, auto-increment | |
| TeamId | int | FK → Team.Id, required, indexed, ON DELETE CASCADE | |
| Name | string(50) | required | |
| AvatarId | int | required, range: 1-20 | References predefined avatar |
| Elo | int | required, default: 1000 | Current ELO rating |
| IsActive | bool | required, default: true | Soft delete flag |
| CreatedAt | DateTimeOffset | required, default: UtcNow | |

### 2.3 Match
| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | PK, auto-increment | |
| TeamId | int | FK → Team.Id, required, indexed (composite with PlayedAt), ON DELETE CASCADE | |
| Team1Score | int | required, range: 0-10 | |
| Team2Score | int | required, range: 0-10 | |
| PlayedAt | DateTimeOffset | required, default: UtcNow | |
| CreatedAt | DateTimeOffset | required, default: UtcNow | |

### 2.4 MatchPlayer
| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | PK, auto-increment | |
| MatchId | int | FK → Match.Id, required, indexed, ON DELETE CASCADE | |
| PlayerId | int | FK → Player.Id, required, ON DELETE RESTRICT | Cannot delete player with match history |
| TeamNumber | int | required, values: 1 or 2 | Which side (Team1 or Team2) |
| Position | string(10) | required, values: "Goalkeeper", "Attacker" | |
| EloChange | int | required | ELO points gained/lost in this match |
| EloBefore | int | required | Player's ELO before this match (for history graphs) |
| EloAfter | int | required | Player's ELO after this match (for history graphs) |

### 2.5 ShareToken
| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | PK, auto-increment | |
| TeamId | int | FK → Team.Id, required, ON DELETE CASCADE | |
| Token | string(64) | required, unique, indexed | URL-safe random token |
| ExpiresAt | DateTimeOffset | required, indexed | When the token becomes invalid |
| CreatedAt | DateTimeOffset | required, default: UtcNow | |

### 2.6 Avatars (Static Data)
Predefined set of 20 avatars (stored as static files, referenced by ID 1-20):
1. ⚽ Soccer Ball
2. 🦁 Lion
3. 🐺 Wolf
4. 🦅 Eagle
5. 🐉 Dragon
6. 🔥 Fire
7. ⚡ Lightning
8. 🌟 Star
9. 🎯 Target
10. 👑 Crown
11. 🦈 Shark
12. 🐻 Bear
13. 🦊 Fox
14. 🐧 Penguin
15. 🦄 Unicorn
16. 🚀 Rocket
17. 💎 Diamond
18. 🎸 Guitar
19. 🌊 Wave
20. 🏆 Trophy

---

## 3. Features

### 3.1 Team Management
- **Create Team**: User provides name, codename (URL slug), and password
  - CodeName: user selects their own, must be unique, min 3 characters
  - CodeName format: lowercase, alphanumeric + hyphens only (auto-formatted during input)
  - Password: min 4 characters
- **Join Team**: User enters CodeName and password
- **Share Team URL**: Button in header to view and copy team URL for sharing with teammates
  - Primary link: Time-limited share link (24 hours) that auto-authenticates users without password
  - Secondary link: Permanent team URL that requires password to join
- **Authentication**: Cookie-based, stores TeamId + hashed token
  - Cookie expiration: 30 days
  - Wrong password: show error
- **Switch Team**: Clear current cookie, redirect to home
- **Password cannot be changed** (v1 simplicity)

### 3.2 Player Management
- **Create Player**: Name + select avatar from grid
  - Name must be unique within team
  - Initial ELO: 1000
- **Edit Player**: Change name or avatar
- **Deactivate Player**: Soft delete (IsActive = false)
  - Deactivated players hidden from dropdowns but visible in history
  - Cannot deactivate player with matches in last 7 days
- **Reactivate Player**: Set IsActive = true
- **Min players per team**: 4 (to allow 2v2)

### 3.3 Match Entry
- **UI Layout**: Visual foosball table representation
  ```
  ┌─────────────────────────────────┐
  │  [P1-GK]  ←→  [P1-ATK]    Team1 │
  │         SCORE: [__]:[__]        │
  │  [P2-GK]  ←→  [P2-ATK]    Team2 │
  └─────────────────────────────────┘
  ```
- **Player Selection**: Click position → dropdown with active players (excluding already selected)
- **Remove Player**: X button appears on hover to remove player from a position
- **Swap Action**: Button between GK/ATK to swap positions within a team
- **Position Labels**: GK/ATK badges shown below each position slot even when player is selected
- **Score Input**: Two number inputs (0-10), wider input (100px) to accommodate two-digit numbers
- **Date**: Optional datetime picker, hidden by default
  - Default behavior: Uses current timestamp when match is submitted
  - Users can click "Set custom date/time" to show the datetime picker for back-dating matches
- **Validation**:
  - All 4 players must be selected and different
  - Scores cannot be equal (no ties in foosball)
  - At least one score must be 10 (standard foosball win condition)
- **Submit Flow**:
  1. Calculate and apply ELO changes
  2. Show success alert with ELO changes summary
  3. "Play Again" button: keeps same players, clears score
  4. "New Match" button: clears everything
- **Edit Match**: Only within 24 hours of creation, recalculates ELO
- **Delete Match**: Only within 24 hours, reverses ELO changes
  - Requires confirmation modal: "Are you sure you want to delete this match? ELO changes will be reversed."
  - Shows match summary (players, score, date) in confirmation dialog
  - After deletion: redirect to match history with success message

### 3.4 ELO Rating System
- **Initial Rating**: 1000
- **K-Factor**: 32 (standard for casual play)
- **Calculation**: Team ELO = average of both players' ELO
- **Formula**:
  ```
  Expected = 1 / (1 + 10^((OpponentElo - PlayerElo) / 400))
  Change = K * (Actual - Expected)
  Actual = 1 for win, 0 for loss
  ```
- **Distribution**: Both winning players get full ELO gain, both losing players lose full amount
- **Floor**: Minimum ELO is 100 (cannot go below)

### 3.5 Statistics

#### Player Statistics
| Stat | Description |
|------|-------------|
| Current ELO | Current rating |
| Highest ELO | All-time peak |
| Lowest ELO | All-time minimum |
| Total Matches | Count of all matches |
| Wins / Losses | Win/loss counts |
| Win Rate | Wins / Total * 100 |
| Current Streak | Consecutive wins (negative for losses) |
| Longest Win Streak | Best consecutive wins ever |
| Preferred Position | Position played >60% of time, or "Flexible" |
| Goals Scored | Sum of team scores when player was Attacker |
| Goals Conceded | Sum of opponent scores when player was Goalkeeper |
| Best Partner | Player with highest win rate when paired (min 3 games) |
| Worst Partner | Player with lowest win rate when paired (min 3 games) |
| Under Table Count | Matches lost with 0 score |

#### Team Statistics (Pairs)
| Stat | Description |
|------|-------------|
| Pair Ranking | All player pairs ranked by win rate (min 3 games) |
| Matches Played | Total matches for the pair |
| Wins / Losses | |
| Win Rate | |
| Average Score | Average points scored per match |

#### Graphs (using Chart.js)
- Player ELO over time (line chart) - data source: `MatchPlayer.EloAfter` ordered by `Match.PlayedAt`
- Win rate by month (bar chart)
- Position distribution (pie chart)

### 3.6 Badges
Badges are dynamic, recalculated on each page load. Each badge displays a tooltip on hover explaining its criteria.

| Badge | Icon | Criteria | Holder |
|-------|------|----------|--------|
| 🔥 Hot Streak | Fire | Currently longest active win streak | Single player |
| 👑 Streak King | Crown | Longest win streak in history | Single player |
| 📉 Last Place | Down arrow | Currently lowest ELO | Single player |
| 🪑 Table Diver | Chair | Most "under the table" losses (0 score) | Single player |
| ⭐ Top Rated | Star | Currently highest ELO | Single player |
| 🛡️ Best Goalkeeper | Shield | Lowest goals conceded per match (min 5 games as GK) | Single player |
| 🎯 Best Attacker | Bullseye | Highest goals scored per match (min 5 games as ATK) | Single player |
| 🆕 Newcomer | Sparkle | Joined in last 7 days | Multiple players |

---

## 4. Pages & Routes

| Route | Page | Auth Required |
|-------|------|---------------|
| `/` | Home - Create or Join team | No |
| `/create` | Create team form | No |
| `/join` | Join team form | No |
| `/{teamCode}` | Team dashboard (landing) | Yes |
| `/{teamCode}/players` | Player management | Yes |
| `/{teamCode}/players/{id}` | Player detail & stats | Yes |
| `/{teamCode}/match/new` | New match entry | Yes |
| `/{teamCode}/matches` | Match history (paginated) | Yes |
| `/{teamCode}/matches/{id}` | Match detail | Yes |
| `/{teamCode}/stats` | Team statistics & graphs | Yes |
| `/{teamCode}/rankings` | Player rankings | Yes |

---

## 5. UI/UX Requirements

### 5.1 General
- **Responsive**: Mobile-first, Tailwind grid/flex utilities
- **Theme**: Light and dark, accent color: green (`--color-brand`, #198754)
- **Loading States**: Spinner overlay for async operations
- **Error Messages**: `.alert` component (`AlertMessage`), auto-dismiss after 5 seconds
- **Validation**: Client-side + server-side, inline error messages

### 5.2 Team Dashboard Layout
```
┌──────────────────────────────────────┐
│ [Logo] Team Name        [+ Match] 👤 │  ← Header
├──────────────────────────────────────┤
│ 🏆 Rankings    📊 Stats    📋 History │  ← Tab navigation
├──────────────────────────────────────┤
│                                      │
│  [Current rankings with badges]      │
│                                      │
│  [Recent matches - last 10]          │
│                                      │
└──────────────────────────────────────┘
```

### 5.3 Match Entry Layout
- **Background**: Use foosball table image as background (top-down view)
- **Player Positions**: 4 circular dropdowns overlaid on the image matching real foosball rod positions:
  ```
  ┌──────────────────────────────────────────────────────────────┐
  │                                                              │
  │  ┌──TEAM 1 SIDE──────────────────────────────────────────┐   │
  │  │                                                       │   │
  │  │   ○ T1 GK        ○ T1 ATK                             │   │
  │  │   (goalie rod)   (3-bar/attack rod)                   │   │
  │  │                                                       │   │
  │  └───────────────────────────────────────────────────────┘   │
  │                                                              │
  │                    ════════════════════                      │
  │                       MIDFIELD LINE                          │
  │                    ════════════════════                      │
  │                                                              │
  │  ┌──TEAM 2 SIDE──────────────────────────────────────────┐   │
  │  │                                                       │   │
  │  │                             ○ T2 ATK      ○ T2 GK     │   │
  │  │                          (3-bar/attack)  (goalie rod) │   │
  │  │                                                       │   │
  │  └───────────────────────────────────────────────────────┘   │
  │                                                              │
  │                      Score: [__] : [__]                      │
  │                                                              │
  └──────────────────────────────────────────────────────────────┘
  
  Team 1: plays TOP side (defends top goal)
  Team 2: plays BOTTOM side (defends bottom goal)
  GK = Goalkeeper rod (closest to own goal)
  ATK = Attacker rod (3-man rod, closer to opponent's goal)
  ```
  Attackers on RIGHT side (attacking opponent's goal)
  ```
- **Circle Styling**:
  - Size: 60px diameter
  - Empty state: White circle with dashed border, "+" icon
  - Filled state: Player avatar + name, solid border
  - Team 1 circles: Yellow border (#ffc107)
  - Team 2 circles: Blue border (#0d6efd)
  - On hover: Slight scale up (1.1x), shadow
  - On click: Opens player dropdown below the circle
- **Swap Buttons**: Small swap icon (⇄) between GK and ATK circles for each team
- **Score Input**: Centered below the table image, large number inputs
- **Image Source**: `foosball-table.jpg` in project root (copy to `/wwwroot/images/foosball-table.jpg`)

### 5.4 Pagination
- Match history: 20 items per page
- Player list: Show all (max 50)
- Rankings: Show all

---

## 6. Tech Stack

### 6.1 Framework
- **Blazor Web App** (.NET 10)
- **Render Mode**: Server-side only (InteractiveServer), no WebAssembly
- **.NET Aspire** for orchestration, local development, and Azure deployment
- **Single Project** structure (plus Aspire AppHost and ServiceDefaults)

### 6.2 Project Structure
```
/Fotbalek.sln
├── /Fotbalek.AppHost              # Aspire orchestration project
│   ├── Program.cs                 # Defines app model (web app + SQL Server)
│   └── appsettings.json
├── /Fotbalek.ServiceDefaults      # Shared Aspire service configuration
│   └── Extensions.cs              # OpenTelemetry, health checks, resilience
├── /Fotbalek.Web                  # Main Blazor Web App
│   ├── /Components
│   │   ├── /Layout
│   │   │   ├── MainLayout.razor
│   │   │   └── NavMenu.razor
│   │   ├── /Pages
│   │   │   ├── Home.razor
│   │   │   ├── /Team
│   │   │   │   ├── Dashboard.razor
│   │   │   │   ├── Players.razor
│   │   │   │   └── ...
│   │   │   └── /Match
│   │   │       ├── NewMatch.razor
│   │   │       └── ...
│   │   └── /Shared
│   │       ├── PlayerCard.razor
│   │       ├── MatchCard.razor
│   │       └── ...
│   ├── /Data
│   │   ├── AppDbContext.cs
│   │   └── /Entities
│   │       ├── Team.cs
│   │       ├── Player.cs
│   │       ├── Match.cs
│   │       └── MatchPlayer.cs
│   ├── /Services
│   │   ├── TeamService.cs
│   │   ├── PlayerService.cs
│   │   ├── MatchService.cs
│   │   ├── EloService.cs
│   │   └── StatsService.cs
│   ├── /Helpers
│   │   ├── SlugGenerator.cs
│   │   └── PasswordHasher.cs
│   ├── Program.cs
│   └── wwwroot/
│       ├── /css
│       ├── /images/avatars/
│       └── /js
└── /Fotbalek.Tests                # (optional) Unit/integration tests
```

### 6.3 Aspire Configuration

#### AppHost Program.cs
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
    .AddDatabase("fotbalek");

builder.AddProject<Projects.Fotbalek_Web>("web")
    .WithReference(sql)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

#### Local Development
- Run `Fotbalek.AppHost` project to start:
  - SQL Server container (via Docker)
  - Blazor Web App with hot reload
  - Aspire Dashboard (OpenTelemetry traces, logs, metrics)
- Connection strings injected automatically via Aspire

#### Azure Deployment
- Use `azd init` + `azd up` for deployment
- Aspire generates Azure resources:
  - **Azure Container Apps** for the web app
  - **Azure SQL Database** for data
- Infrastructure as code via Bicep (auto-generated)

### 6.4 Database
- **Local**: SQL Server container (managed by Aspire)
- **Production**: Azure SQL Database
- **EF Core 10** with Code-First migrations
- **Connection String**: Injected by Aspire (no manual configuration needed)

### 6.5 Database Indexes
```sql
CREATE UNIQUE INDEX IX_Team_CodeName ON Team(CodeName);
CREATE INDEX IX_Player_TeamId ON Player(TeamId);
CREATE INDEX IX_Match_TeamId_PlayedAt ON Match(TeamId, PlayedAt DESC);
CREATE INDEX IX_MatchPlayer_MatchId ON MatchPlayer(MatchId);
CREATE INDEX IX_MatchPlayer_PlayerId ON MatchPlayer(PlayerId);
```

### 6.6 DateTime Handling
- **Storage**: All dates stored as `DateTimeOffset` (preserves timezone)
- **Server**: Store in UTC (`DateTimeOffset.UtcNow`)
- **UI Display**: Convert to browser's local time using JavaScript
  ```javascript
  // In Blazor, use JS interop to format dates
  new Date(utcDateString).toLocaleString()
  ```
- **Date Picker**: Send local time from browser, convert to UTC on server

### 6.7 Dependencies

#### Fotbalek.Web
```xml
<PackageReference Include="Aspire.Microsoft.EntityFrameworkCore.SqlServer" Version="9.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="10.*" />
<!-- No additional packages needed for password hashing - using built-in ASP.NET Core Identity hasher -->
```

#### Fotbalek.AppHost
```xml
<PackageReference Include="Aspire.Hosting.AppHost" Version="9.*" />
<PackageReference Include="Aspire.Hosting.SqlServer" Version="9.*" />
```

### 6.8 Frontend
- **Tailwind CSS 4** — `Styles/app.css` compiles to `wwwroot/app.css`; design tokens
  live in `Styles/theme.css`, reusable classes in `Styles/components/`. Built by
  `npm run css`, and by `dotnet build` when Node is available.
- **Bootstrap Icons** (self-hosted in `wwwroot/lib`) - the icon font only; no Bootstrap CSS or JS
- **Chart.js 4** (self-hosted in `wwwroot/lib`) - for statistics graphs

Everything the browser loads is self-hosted — no CDN. `npm run vendor` refreshes
`wwwroot/lib` from `node_modules` after a version bump; the result is committed.

---

## 7. Security

> **Note**: Minimal security for PoC. Harden before production use.

- **Password Storage**: ASP.NET Core Identity `PasswordHasher<T>` (built-in)
- **Cookie**: HttpOnly, SameSite=Lax
- **Input Validation**: Basic validation via EF Core and Blazor
- **SQL Injection**: Prevented via EF Core parameterized queries
- **XSS**: Blazor's built-in encoding

---

## 8. Error Handling

| Scenario | Behavior |
|----------|----------|
| Invalid team code | Redirect to `/` with "Team not found" message |
| Wrong password | Show inline error |
| Expired/invalid cookie | Redirect to `/join?team={code}` |
| Database error | Show generic "Something went wrong" |
| Validation error | Highlight field, show specific message |
