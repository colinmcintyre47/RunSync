using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RunSync.Api.Data;
using RunSync.Api.Models.DTOs.Training;
using RunSync.Api.Models.Entities;
using RunSync.Api.Services.Interfaces;

namespace RunSync.Api.Services;

public class TrainingPlanService : ITrainingPlanService
{
    private readonly RunSyncDbContext _db;

    public TrainingPlanService(RunSyncDbContext db) => _db = db;

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<TrainingGoalDto?> GetGoalAsync(int userId)
    {
        var plan = await _db.UserTrainingPlans.FirstOrDefaultAsync(p => p.UserId == userId);
        return plan is null ? null : MapToGoalDto(plan);
    }

    public async Task<TrainingGoalDto> SaveGoalAsync(int userId, TrainingGoalInputDto dto)
    {
        var plan = await _db.UserTrainingPlans.FirstOrDefaultAsync(p => p.UserId == userId);

        if (plan is null)
        {
            plan = new UserTrainingPlan { UserId = userId, CreatedAt = DateTime.UtcNow };
            _db.UserTrainingPlans.Add(plan);
        }

        plan.GoalType            = dto.GoalType;
        plan.RaceDate            = dto.RaceDate.Date;
        plan.FitnessLevel        = dto.FitnessLevel;
        plan.CurrentWeeklyMiles  = dto.CurrentWeeklyMiles;
        plan.CurrentLongRunMiles = dto.CurrentLongRunMiles;
        plan.GoalFinishMinutes   = dto.GoalFinishMinutes;
        plan.RunDays             = JsonSerializer.Serialize(dto.RunDays);
        plan.LongRunDay          = dto.LongRunDay;
        plan.UpdatedAt           = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return MapToGoalDto(plan);
    }

    public async Task DeleteGoalAsync(int userId)
    {
        var plan = await _db.UserTrainingPlans.FirstOrDefaultAsync(p => p.UserId == userId);
        if (plan is not null)
        {
            _db.UserTrainingPlans.Remove(plan);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<TrainingPlanDto?> GetTrainingPlanAsync(int userId)
    {
        var plan = await _db.UserTrainingPlans.FirstOrDefaultAsync(p => p.UserId == userId);
        if (plan is null) return null;

        var activities = await _db.StravaActivities
            .Where(a => a.UserId == userId && (a.Type == "Run" || a.Type == "VirtualRun" || a.SportType == "Run"))
            .ToListAsync();

        return GeneratePlan(plan, activities);
    }

    // ── Plan Generation ───────────────────────────────────────────────────────

    private static TrainingPlanDto GeneratePlan(UserTrainingPlan goal, List<StravaActivity> activities)
    {
        var today      = DateTime.Today;
        var raceDate   = goal.RaceDate.Date;
        var runDays    = JsonSerializer.Deserialize<List<string>>(goal.RunDays) ?? new List<string>();
        var (planWeeks, maxDays, peakLong, minWeekly) = GetPlanParams(goal.GoalType, goal.FitnessLevel);

        // Plan starts on the Monday that is `planWeeks` weeks before race date
        var planStart    = GetMondayOf(raceDate.AddDays(-planWeeks * 7));
        var weeklyMiles  = BuildWeeklyMiles(goal.CurrentWeeklyMiles, minWeekly, peakLong, planWeeks);
        var longRunMiles = BuildLongRunMiles(goal.CurrentLongRunMiles, peakLong, planWeeks);

        // Index Strava activities by local date for O(1) lookup
        var activityByDate = activities
            .GroupBy(a => a.StartDateLocal.Date)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.DistanceMeters).First());

        var weeks = new List<TrainingWeekDto>();
        int currentWeekNum = 0;

        for (int w = 1; w <= planWeeks; w++)
        {
            var weekStart = planStart.AddDays((w - 1) * 7);
            var weekEnd   = weekStart.AddDays(6);
            var phase     = GetPhase(w, planWeeks);
            var isCutback = IsCutbackWeek(w);
            var targetMi  = weeklyMiles[w - 1];
            var longMi    = longRunMiles[w - 1];
            var isCurrentWeek = today >= weekStart && today <= weekEnd;

            if (isCurrentWeek) currentWeekNum = w;

            // Determine run days for this week
            var assignedDays = AssignWorkouts(weekStart, runDays, goal.LongRunDay, maxDays,
                                              targetMi, longMi, phase, activityByDate, today);

            int plannedRuns   = assignedDays.Count(d => d.IsRunDay);
            int completedRuns = assignedDays.Count(d => d.Status == "completed");

            weeks.Add(new TrainingWeekDto
            {
                WeekNumber    = w,
                WeekLabel     = $"Week {w} · {phase}",
                Phase         = phase,
                WeekStart     = weekStart.ToString("yyyy-MM-dd"),
                WeekEnd       = weekEnd.ToString("yyyy-MM-dd"),
                TargetMiles   = MathF.Round(targetMi, 1),
                IsCutbackWeek = isCutback,
                IsCurrentWeek = isCurrentWeek,
                PlannedRuns   = plannedRuns,
                CompletedRuns = completedRuns,
                Days          = assignedDays,
            });
        }

        if (currentWeekNum == 0)
            currentWeekNum = today < planStart ? 0 : planWeeks;

        var totalPlanned   = weeks.Sum(w => w.PlannedRuns);
        var totalCompleted = weeks.Where(w => w.WeekStart.CompareTo(today.ToString("yyyy-MM-dd")) <= 0)
                                  .Sum(w => w.CompletedRuns);
        var pastPlanned    = weeks.Where(w => w.WeekEnd.CompareTo(today.ToString("yyyy-MM-dd")) < 0)
                                  .Sum(w => w.PlannedRuns);
        double completion  = pastPlanned > 0 ? (double)totalCompleted / pastPlanned : 0;

        return new TrainingPlanDto
        {
            Goal = MapToGoalDto(goal, planWeeks),
            CurrentWeekNumber    = currentWeekNum,
            OverallCompletionRate = Math.Round(completion, 3),
            Weeks = weeks,
        };
    }

    // ── Day Assignment ────────────────────────────────────────────────────────

    private static List<TrainingDayDto> AssignWorkouts(
        DateTime weekStart,
        List<string> preferredDays,
        string longRunDay,
        int maxDays,
        float targetMiles,
        float longRunMiles,
        string phase,
        Dictionary<DateTime, StravaActivity> activityByDate,
        DateTime today)
    {
        // Map day abbreviations to DayOfWeek
        var dayMap = new Dictionary<string, DayOfWeek>
        {
            ["Mon"] = DayOfWeek.Monday, ["Tue"] = DayOfWeek.Tuesday,
            ["Wed"] = DayOfWeek.Wednesday, ["Thu"] = DayOfWeek.Thursday,
            ["Fri"] = DayOfWeek.Friday, ["Sat"] = DayOfWeek.Saturday,
            ["Sun"] = DayOfWeek.Sunday,
        };
        var revMap = dayMap.ToDictionary(kv => kv.Value, kv => kv.Key);

        // Normalize preferred run days, sort Mon–Sun
        var sortedRunDays = preferredDays
            .Where(dayMap.ContainsKey)
            .Select(d => dayMap[d])
            .OrderBy(d => d == DayOfWeek.Sunday ? 7 : (int)d)
            .ToList();

        // Clamp to maxDays, always keeping the long run day
        var longRunDow = dayMap.GetValueOrDefault(longRunDay, DayOfWeek.Sunday);
        if (!sortedRunDays.Contains(longRunDow) && sortedRunDays.Count > 0)
            sortedRunDays[^1] = longRunDow;

        if (sortedRunDays.Count > maxDays)
        {
            var without = sortedRunDays.Where(d => d != longRunDow).ToList();
            var keep    = without.Take(maxDays - 1).ToList();
            keep.Add(longRunDow);
            sortedRunDays = keep.OrderBy(d => d == DayOfWeek.Sunday ? 7 : (int)d).ToList();
        }

        // Assign workout types
        var qualitySlots = new Queue<string>(GetQualityWorkouts(sortedRunDays.Count, phase));
        var workoutForDay = new Dictionary<DayOfWeek, PlannedRunDto>();

        foreach (var dow in sortedRunDays)
        {
            if (dow == longRunDow)
            {
                workoutForDay[dow] = MakeLongRun(longRunMiles);
            }
            else
            {
                var type = qualitySlots.Count > 0 ? qualitySlots.Dequeue() : "easy";
                workoutForDay[dow] = MakePlannedRun(type, targetMiles, longRunMiles, sortedRunDays.Count);
            }
        }

        // Build 7 days of the week
        var days = new List<TrainingDayDto>();
        for (int d = 0; d < 7; d++)
        {
            var date   = weekStart.AddDays(d);
            var dow    = date.DayOfWeek;
            var abbrev = revMap.GetValueOrDefault(dow, "Mon");
            var isRun  = workoutForDay.ContainsKey(dow);

            string status;
            CompletedActivityDto? completed = null;

            if (date.Date == today.Date)
            {
                status = "today";
                if (activityByDate.TryGetValue(date.Date, out var act))
                {
                    status    = "completed";
                    completed = MapActivity(act);
                }
            }
            else if (date.Date < today.Date)
            {
                if (isRun && activityByDate.TryGetValue(date.Date, out var act))
                {
                    status    = "completed";
                    completed = MapActivity(act);
                }
                // Check day before/after tolerance for planned run days
                else if (isRun && (
                    activityByDate.ContainsKey(date.Date.AddDays(-1)) ||
                    activityByDate.ContainsKey(date.Date.AddDays(1))))
                {
                    var neighbour = activityByDate.GetValueOrDefault(date.Date.AddDays(-1))
                                 ?? activityByDate.GetValueOrDefault(date.Date.AddDays(1));
                    status    = "completed";
                    completed = MapActivity(neighbour!);
                }
                else
                {
                    status = isRun ? "missed" : "rest";
                }
            }
            else
            {
                status = isRun ? "upcoming" : "rest";
            }

            days.Add(new TrainingDayDto
            {
                Date        = date.ToString("yyyy-MM-dd"),
                DayOfWeek   = abbrev,
                IsRunDay    = isRun,
                Status      = status,
                PlannedRun  = isRun ? workoutForDay[dow] : null,
                RestTip     = isRun ? null : GetRestTip(date),
                CompletedActivity = completed,
            });
        }

        return days;
    }

    // ── Workout builders ──────────────────────────────────────────────────────

    // Returns ordered quality slot types (non-long-run days) for the week.
    // Position 0 = first non-long run day, etc.
    private static string[] GetQualityWorkouts(int runCount, string phase) =>
        (runCount, phase) switch
        {
            (3, "Base")         => new[] { "easy", "easy_strides" },
            (3, _)              => new[] { "easy", "tempo" },
            (4, "Base")         => new[] { "easy", "easy_strides", "easy" },
            (4, "Build")        => new[] { "easy", "tempo", "easy" },
            (4, "Peak")         => new[] { "easy", "intervals", "easy" },
            (4, "Taper")        => new[] { "easy", "easy_strides", "easy" },
            (5, "Base")         => new[] { "easy", "easy_strides", "easy", "easy" },
            (5, "Build")        => new[] { "easy", "tempo", "recovery", "easy" },
            (5, _)              => new[] { "easy", "intervals", "recovery", "tempo" },
            (6, _)              => new[] { "easy", "intervals", "recovery", "tempo", "easy" },
            _                   => new[] { "easy" },
        };

    private static PlannedRunDto MakePlannedRun(string type, float weeklyMiles, float longRunMiles, int runCount)
    {
        float easyTotal = weeklyMiles - longRunMiles;
        float easyCount = MathF.Max(1, runCount - 1);
        float easyMiles = MathF.Max(2.5f, easyTotal / easyCount);

        return type switch
        {
            "tempo" => new PlannedRunDto
            {
                RunType      = "Tempo Run",
                TargetMiles  = MathF.Round(MathF.Max(3f, weeklyMiles * 0.18f), 1),
                EffortLevel  = "Moderate-Hard",
                HrZone       = 4,
                Description  = "Comfortably hard effort — labored breathing, 3–5 words per breath. Sustainable for 20–30 minutes.",
                PaceGuidance = "~30 sec/mi faster than easy pace",
            },
            "intervals" => new PlannedRunDto
            {
                RunType      = "Intervals",
                TargetMiles  = MathF.Round(MathF.Max(2f, weeklyMiles * 0.12f), 1),
                EffortLevel  = "Hard",
                HrZone       = 5,
                Description  = "400–800m repeats at hard effort with equal recovery jog. Include 1mi warm-up and cool-down.",
                PaceGuidance = "Near max effort per repeat",
            },
            "easy_strides" => new PlannedRunDto
            {
                RunType      = "Easy Run + Strides",
                TargetMiles  = MathF.Round(easyMiles, 1),
                EffortLevel  = "Easy",
                HrZone       = 2,
                Description  = "Easy conversational run finishing with 4–6 × 20-second accelerations (strides) at about mile race pace.",
                PaceGuidance = "Conversational pace + short fast bursts",
            },
            "recovery" => new PlannedRunDto
            {
                RunType      = "Recovery Run",
                TargetMiles  = MathF.Round(MathF.Max(2f, easyMiles * 0.7f), 1),
                EffortLevel  = "Very Easy",
                HrZone       = 1,
                Description  = "Slower than your easy pace. Legs-only movement to flush out fatigue from yesterday's workout.",
                PaceGuidance = "60–90 sec/mi slower than easy",
            },
            _ => new PlannedRunDto
            {
                RunType      = "Easy Run",
                TargetMiles  = MathF.Round(easyMiles, 1),
                EffortLevel  = "Easy",
                HrZone       = 2,
                Description  = "Fully conversational pace — you should be able to speak in full sentences the entire run.",
                PaceGuidance = "Comfortable, nose-breathing pace",
            },
        };
    }

    private static PlannedRunDto MakeLongRun(float miles) => new()
    {
        RunType      = "Long Run",
        TargetMiles  = MathF.Round(miles, 1),
        EffortLevel  = "Easy",
        HrZone       = 2,
        Description  = "Your most important weekly run. Conversational pace throughout — resist the urge to push. Fuel every 45–60 min on runs over 75 min.",
        PaceGuidance = "Same easy conversational pace as daily runs",
    };

    // ── Mileage Progression ───────────────────────────────────────────────────

    private static float[] BuildWeeklyMiles(float current, float minWeekly, float peakLong, int planWeeks)
    {
        float maxWeekly = peakLong / 0.30f;
        float start     = MathF.Max(MathF.Max(current, minWeekly * 0.60f), 8f);
        float peak      = 0f;

        var targets = new float[planWeeks];
        targets[0] = start;

        for (int i = 1; i < planWeeks - 2; i++)
        {
            targets[i] = IsCutbackWeek(i + 1)
                ? targets[i - 1] * 0.80f
                : MathF.Min(targets[i - 1] * 1.10f, maxWeekly);
            peak = MathF.Max(peak, targets[i]);
        }

        if (peak == 0) peak = targets[^3];

        // Taper
        targets[^2] = MathF.Round(peak * 0.65f, 1);
        targets[^1] = MathF.Round(peak * 0.40f, 1);  // race week

        return targets.Select(t => MathF.Round(t, 1)).ToArray();
    }

    private static float[] BuildLongRunMiles(float current, float peakLong, int planWeeks)
    {
        float start = MathF.Max(current, 3f);
        var longs   = new float[planWeeks];
        longs[0]    = MathF.Min(start, peakLong);

        for (int i = 1; i < planWeeks - 2; i++)
        {
            longs[i] = IsCutbackWeek(i + 1)
                ? longs[i - 1]
                : MathF.Min(longs[i - 1] + 1.0f, peakLong);
        }

        // Taper long runs
        float peakLongActual = longs[^3];
        longs[^2] = MathF.Round(peakLongActual * 0.70f, 1);
        longs[^1] = MathF.Round(peakLongActual * 0.45f, 1);

        return longs.Select(l => MathF.Round(l, 1)).ToArray();
    }

    // ── Plan Parameters ───────────────────────────────────────────────────────

    private static (int weeks, int maxDays, float peakLong, float minWeekly) GetPlanParams(
        string goalType, string fitnessLevel)
    {
        return (goalType, fitnessLevel) switch
        {
            ("FiveK",        "Beginner")     => (8,  3, 4f,  8f),
            ("FiveK",        "Intermediate") => (10, 4, 6f,  15f),
            ("FiveK",        "Advanced")     => (10, 4, 7f,  22f),
            ("TenK",         "Beginner")     => (10, 3, 6f,  12f),
            ("TenK",         "Intermediate") => (12, 4, 8f,  20f),
            ("TenK",         "Advanced")     => (12, 5, 10f, 28f),
            ("HalfMarathon", "Beginner")     => (12, 4, 10f, 15f),
            ("HalfMarathon", "Intermediate") => (16, 5, 12f, 25f),
            ("HalfMarathon", "Advanced")     => (16, 5, 13f, 35f),
            ("Marathon",     "Beginner")     => (16, 4, 18f, 25f),
            ("Marathon",     "Intermediate") => (18, 5, 20f, 35f),
            ("Marathon",     "Advanced")     => (20, 6, 22f, 50f),
            _                               => (12, 4, 10f, 15f),
        };
    }

    private static string GetPhase(int weekNum, int totalWeeks)
    {
        int baseCutoff  = (int)Math.Ceiling(totalWeeks * 0.30);
        int buildCutoff = (int)Math.Ceiling(totalWeeks * 0.65);

        if (weekNum >= totalWeeks - 1) return "Taper";
        if (weekNum <= baseCutoff)     return "Base";
        if (weekNum <= buildCutoff)    return "Build";
        return "Peak";
    }

    private static bool IsCutbackWeek(int weekNum) => weekNum % 4 == 0;

    private static DateTime GetMondayOf(DateTime date)
    {
        int diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-diff).Date;
    }

    // ── Strava Activity → DTO ─────────────────────────────────────────────────

    private static CompletedActivityDto MapActivity(StravaActivity a)
    {
        float miles = a.DistanceMeters / 1609.344f;
        string pace = a.MovingTimeSeconds > 0 && miles > 0
            ? FormatPace(a.MovingTimeSeconds / miles)
            : "—";
        return new CompletedActivityDto
        {
            Miles        = MathF.Round(miles, 2),
            Pace         = pace,
            ActivityName = a.Name,
        };
    }

    private static string FormatPace(float secondsPerMile)
    {
        int total   = (int)Math.Round(secondsPerMile);
        int minutes = total / 60;
        int seconds = total % 60;
        return $"{minutes}:{seconds:D2}/mi";
    }

    // ── Goal DTO Mapping ──────────────────────────────────────────────────────

    private static TrainingGoalDto MapToGoalDto(UserTrainingPlan plan, int planWeeks = 0)
    {
        var runDays = JsonSerializer.Deserialize<List<string>>(plan.RunDays) ?? new List<string>();
        if (planWeeks == 0)
            (planWeeks, _, _, _) = GetPlanParams(plan.GoalType, plan.FitnessLevel);

        return new TrainingGoalDto
        {
            GoalType           = plan.GoalType,
            GoalLabel          = GoalLabel(plan.GoalType),
            RaceDate           = plan.RaceDate.ToString("yyyy-MM-dd"),
            FitnessLevel       = plan.FitnessLevel,
            CurrentWeeklyMiles = plan.CurrentWeeklyMiles,
            CurrentLongRunMiles= plan.CurrentLongRunMiles,
            GoalFinishMinutes  = plan.GoalFinishMinutes,
            RunDays            = runDays,
            LongRunDay         = plan.LongRunDay,
            DaysToRace         = (int)Math.Ceiling((plan.RaceDate.Date - DateTime.Today).TotalDays),
            TotalPlanWeeks     = planWeeks,
        };
    }

    private static string GoalLabel(string goalType) => goalType switch
    {
        "FiveK"        => "5K",
        "TenK"         => "10K",
        "HalfMarathon" => "Half Marathon",
        "Marathon"     => "Marathon",
        _              => goalType,
    };

    // ── Science Tips ──────────────────────────────────────────────────────────

    private static RestTipDto GetRestTip(DateTime date)
    {
        int index = Math.Abs(date.DayOfYear + (int)date.DayOfWeek) % Tips.Length;
        return Tips[index];
    }

    private static readonly RestTipDto[] Tips =
    {
        new() {
            Category = "Nutrition",
            Headline = "Carbs are your running fuel",
            Detail   = "Carbohydrates provide the fastest energy source for muscles. Aim for 6–10g of carbs per kg of body weight on high-mileage days to keep glycogen stores full.",
            Source   = "Burke et al., International Journal of Sport Nutrition, 2011",
        },
        new() {
            Category = "Sleep",
            Headline = "Sleep is the #1 recovery tool",
            Detail   = "Growth hormone — the primary signal that repairs muscle — is released 70% during deep sleep. Distance runners benefit from 8–10 hours. Even one night under 6 hours reduces time-to-exhaustion by up to 30%.",
            Source   = "Mah et al., Sleep, 2011 · Oliver et al., Sleep, 2009",
        },
        new() {
            Category = "Recovery",
            Headline = "Active rest beats doing nothing",
            Detail   = "Light walking or easy cycling on rest days increases blood flow and clears lactate 25% faster than passive rest, reducing next-day soreness without adding training load.",
            Source   = "Weerapong et al., Sports Medicine, 2005",
        },
        new() {
            Category = "Nutrition",
            Headline = "Post-run protein window",
            Detail   = "Consuming 20–40g of protein within 30 minutes of finishing a run maximizes muscle protein synthesis. Chocolate milk, Greek yogurt, or a protein shake all qualify.",
            Source   = "Phillips & Van Loon, Journal of Sports Sciences, 2011",
        },
        new() {
            Category = "Strength",
            Headline = "Two lifts a week improve economy",
            Detail   = "Just 2 sessions per week of lower-body strength training improves running economy by 2–8% and cuts injury risk in half. Focus on single-leg movements that mirror the running gait.",
            Source   = "Beattie et al., Journal of Strength and Conditioning Research, 2014",
        },
        new() {
            Category = "Sleep",
            Headline = "Consistent sleep timing matters",
            Detail   = "Going to bed and waking at the same time every day — even weekends — regulates your circadian rhythm, improving sleep quality and recovery without needing to sleep longer.",
            Source   = "Czeisler et al., New England Journal of Medicine, 2005",
        },
        new() {
            Category = "Nutrition",
            Headline = "Hydration: watch the colour",
            Detail   = "Pale yellow urine throughout the day indicates good hydration. Drink 400–600ml of water 2–3 hours before running. Avoid overhydrating — drinking only when thirsty during runs is evidence-backed for most runners.",
            Source   = "ACSM Position Stand on Exercise and Fluid Replacement, 2007",
        },
        new() {
            Category = "Recovery",
            Headline = "Foam roll for 90 seconds",
            Detail   = "Self-myofascial release for at least 90 seconds per muscle group reduces DOMS and improves range of motion without impairing strength. Target calves, quads, IT band, and hip flexors.",
            Source   = "Cheatham et al., International Journal of Sports Physical Therapy, 2015",
        },
        new() {
            Category = "Mental",
            Headline = "Self-talk measurably improves endurance",
            Detail   = "Motivational self-talk (\"come on\", \"strong\") improves endurance performance by 4–8% by reducing perceived effort. Write 2–3 personal cue phrases and rehearse them before hard workouts.",
            Source   = "Hatzigeorgiadis et al., Perspectives on Psychological Science, 2011",
        },
        new() {
            Category = "Nutrition",
            Headline = "Electrolytes on long runs",
            Detail   = "Sodium lost in sweat can cause cramping and hyponatremia. Use an electrolyte drink or salt tabs on runs over 75–90 minutes. Aim for 500–700mg sodium per hour of running.",
            Source   = "Maughan & Shirreffs, British Journal of Sports Medicine, 2010",
        },
        new() {
            Category = "Strength",
            Headline = "Cadence: aim for 170–180 spm",
            Detail   = "Running at a cadence of 170–180 steps per minute reduces ground impact forces by up to 30% and significantly lowers injury risk, particularly to knees and hips.",
            Source   = "Heiderscheit et al., Medicine & Science in Sports & Exercise, 2011",
        },
        new() {
            Category = "Recovery",
            Headline = "The 10% mileage rule",
            Detail   = "Increasing weekly mileage by more than 10% from one week to the next significantly raises overuse injury risk. Your training plan follows this rule — trust the process and don't double up missed days.",
            Source   = "van Gent et al., British Journal of Sports Medicine, 2007",
        },
        new() {
            Category = "Mental",
            Headline = "Visualise your race",
            Detail   = "Mental rehearsal activates the same motor pathways as physical practice. Spend 10 minutes before sleep visualising crossing the finish line — the sights, sounds, and how your body feels at race pace.",
            Source   = "Moran, Applied Sport Psychology, 2012",
        },
        new() {
            Category = "Nutrition",
            Headline = "Iron: the hidden limiter",
            Detail   = "Distance runners are at high risk of iron deficiency, which impairs oxygen transport and tanks performance. Pair iron-rich foods (red meat, lentils, spinach) with vitamin C to boost absorption by up to 3×.",
            Source   = "Peeling et al., International Journal of Sport Nutrition, 2008",
        },
        new() {
            Category = "Recovery",
            Headline = "Cold water immersion",
            Detail   = "Ice baths at 11–15°C for 10–15 minutes after hard sessions reduce DOMS by ~20% and accelerate next-day performance recovery. Even cold showers provide a measurable benefit.",
            Source   = "Leeder et al., British Journal of Sports Medicine, 2012",
        },
        new() {
            Category = "Strength",
            Headline = "Yoga improves recovery",
            Detail   = "8 weeks of regular yoga practice significantly improves flexibility, balance, and reduces DOMS in distance runners. Even 15 minutes of post-run stretching yields measurable benefits.",
            Source   = "Mallinson et al., Evidence-Based Complementary and Alternative Medicine, 2015",
        },
        new() {
            Category = "Nutrition",
            Headline = "Pre-run fuelling timing",
            Detail   = "Eat 1–4g of carbohydrate per kg of body weight 1–4 hours before long runs. A banana with peanut butter toast 90 minutes before is a well-tested combination that minimises GI distress.",
            Source   = "Burke et al., International Journal of Sport Nutrition, 2011",
        },
        new() {
            Category = "Sleep",
            Headline = "Pre-sleep protein",
            Detail   = "40g of casein protein (cottage cheese, Greek yogurt) before bed stimulates overnight muscle protein synthesis — especially beneficial on days with hard training or heavy mileage.",
            Source   = "Res et al., Medicine & Science in Sports & Exercise, 2012",
        },
        new() {
            Category = "Mental",
            Headline = "Establish a pre-race routine",
            Detail   = "Pre-race routines reduce anxiety and prime performance by creating predictable arousal. Practice your exact race morning routine — breakfast, warmup, timing — during your long run days.",
            Source   = "Cotterill, Journal of Applied Sport Psychology, 2010",
        },
        new() {
            Category = "Recovery",
            Headline = "Compression socks work",
            Detail   = "Graduated compression socks reduce muscle soreness by 10–15% and improve calf venous return. Wear them for 12–24 hours post-long run for maximum recovery benefit.",
            Source   = "Hill et al., Journal of Strength and Conditioning Research, 2014",
        },
        new() {
            Category = "Nutrition",
            Headline = "Beetroot juice for performance",
            Detail   = "Dietary nitrates in beetroot juice improve running economy by 2–3% and raise VO₂ max. Drink 300–500ml of beetroot juice 2–3 hours before hard workouts or race day.",
            Source   = "Jones et al., Journal of Applied Physiology, 2010",
        },
        new() {
            Category = "Strength",
            Headline = "Single-leg > two-leg exercises",
            Detail   = "Running is entirely a single-leg activity. Step-ups, Bulgarian split squats, and single-leg calf raises transfer strength gains to running far more effectively than bilateral squats or leg presses.",
            Source   = "Storen et al., Journal of Strength and Conditioning Research, 2008",
        },
        new() {
            Category = "Recovery",
            Headline = "Taper is science, not laziness",
            Detail   = "A 2–3 week taper — reducing volume 40–60% while maintaining intensity — improves race performance by an average of 2–3%. The fitness you built doesn't disappear; your body is consolidating it.",
            Source   = "Bosquet et al., Medicine & Science in Sports & Exercise, 2007",
        },
        new() {
            Category = "Mental",
            Headline = "Negative splits beat even pace",
            Detail   = "Starting a race 5% slower than goal pace and finishing faster (negative split) consistently produces better overall times than even pacing — confirmed across thousands of marathon finishers.",
            Source   = "Renfree & St Clair Gibson, International Journal of Sports Physiology, 2013",
        },
        new() {
            Category = "Nutrition",
            Headline = "Carb-load for races over 90 min",
            Detail   = "For half marathons and marathons, 3–4 days of high-carbohydrate intake (8–10g/kg/day) fully saturates glycogen stores and can improve race time by 2–3%.",
            Source   = "Burke et al., International Journal of Sport Nutrition, 2011",
        },
        new() {
            Category = "Sleep",
            Headline = "Sleep debt compounds",
            Detail   = "Chronic mild sleep restriction (6 hours/night for 2 weeks) impairs performance as much as total sleep deprivation for 24 hours — but people adapt to feeling 'fine' without realising the deficit.",
            Source   = "Van Dongen et al., Sleep, 2003",
        },
        new() {
            Category = "Strength",
            Headline = "Hip strength prevents injuries",
            Detail   = "Weakness in hip abductors (glutes) is linked to IT band syndrome, patellofemoral pain, and shin splints. Clamshells, lateral band walks, and hip thrusts 2× per week address the root cause.",
            Source   = "Fredericson & Wolf, Clinical Journal of Sport Medicine, 2005",
        },
        new() {
            Category = "Recovery",
            Headline = "Vary your running surfaces",
            Detail   = "Alternating between pavement, trails, and track distributes impact stress across different muscle groups and reduces repetitive strain injuries. Aim for at least one off-road run per week.",
            Source   = "Hreljac, Current Sports Medicine Reports, 2005",
        },
    };
}
