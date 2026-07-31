# Connecting Strava to RunSync

**Time needed:** about a minute. You only do this once.

---

## Why you have to do this

Strava gives every API application a limit of **one athlete**. That is not a RunSync setting —
it is Strava's rule for free applications.

If everyone shared one RunSync application, only one person in the world could use RunSync.
So instead, each person creates their own free Strava application. You are the only athlete on
yours, which is exactly what Strava allows, and RunSync stays free for everyone.

You are not signing up for anything, paying anything, or making your data public. A Strava API
application is just a set of keys that lets RunSync read *your* activities on *your* behalf.

---

## Step 1 — Create your Strava application

Go to **[strava.com/settings/api](https://www.strava.com/settings/api)** while signed in to Strava.

Fill in the form. Nothing here is public or permanent, so don't overthink it:

| Field | What to put |
|-------|-------------|
| **Application Name** | `RunSync` (or anything you like) |
| **Category** | `Training` |
| **Club** | Leave blank |
| **Website** | Any URL — your Strava profile is fine |
| **Application Description** | Anything, or leave blank |
| **Authorization Callback Domain** | **See step 2 — this one matters** |

Tick the agreement box and click **Create**.

> If Strava asks you to upload an app icon, any image works. It only ever appears on your own
> authorization screen.

---

## Step 2 — Set the Authorization Callback Domain

This is the one field that has to be exactly right, and it's the one people get wrong.

Open RunSync → **Settings**. Under "Your Strava application" you'll see the exact value with a
**Copy** button next to it. Paste that into Strava's **Authorization Callback Domain** field.

Two things to watch for:

- It's a **domain only** — no `https://`, no `/api/strava/callback` on the end, no port number.
  Strava rejects anything else.
- Copy it from your own RunSync Settings screen rather than from this document. The correct
  value depends on where RunSync is deployed.

Click **Update** on Strava to save.

---

## Step 3 — Copy your two keys into RunSync

Still on [strava.com/settings/api](https://www.strava.com/settings/api), you'll now see:

- **Client ID** — a short number, something like `123456`
- **Client Secret** — a long string of 40 letters and numbers. You may need to click
  **"Show"** to reveal it.

In RunSync → **Settings**, paste both into the matching fields and click **Save application**.

RunSync encrypts your Client Secret before storing it, and never shows it again. If you ever
need to change it, you paste in a fresh one — the same way password fields work.

---

## Step 4 — Connect and sync

1. Go back to the training plan and click **Connect Strava**
2. Strava asks you to authorize RunSync — click **Authorize**
3. You'll land back in RunSync with a green "Strava connected" message
4. Click **Sync Now** to pull in your runs

The first sync pulls in your **entire** Strava run history, so it may take a few seconds. From
then on, click **Sync Now** whenever you want to pull in new activities.

---

## What you'll see

**All runs** (the default tab) — every run you've ever logged, grouped by week, newest first.
Each row shows distance, pace, moving time, and average heart rate, and links back to the
activity on Strava.

**Training plan** — the same runs laid over a structured plan, showing planned vs. actual
mileage for each day.

**The four tiles at the top** — miles this week, runs this week, all-time miles, and your
weekly streak (consecutive Mon–Sun weeks with at least one run).

---

## Copying your data out

The **Copy all data** button puts every synced run on your clipboard as spreadsheet columns.
Paste it straight into Google Sheets or Excel — each field lands in its own column, no import
dialog needed. You get 19 columns per run: date, week, name, sport and workout type, miles,
moving and elapsed time, pace, average and max heart rate, elevation gain, cadence, suffer
score, effort level, PR count, whether it was a manual entry, and the Strava ID and link.

Next to it, **as JSON** copies the complete structured export instead — the same runs plus your
athlete profile, summary stats, and each run's encoded route. Use that one if you're feeding
the data into another tool.

Both buttons confirm how many runs were copied, so you'll know it worked.

---

## If something goes wrong

**"Strava rejected your API application credentials"**

Almost always one of three things:

- The Client Secret was pasted incorrectly — go back to
  [strava.com/settings/api](https://www.strava.com/settings/api), click **Show**, and copy it
  again. It's 40 characters with no spaces.
- The Client ID and Client Secret came from two different applications.
- The Authorization Callback Domain doesn't match. Re-check step 2 — it's the most common cause.

**"Strava authorization was cancelled"**

You clicked Cancel instead of Authorize on Strava's screen. Just click **Connect Strava** again.

**Strava's authorization page shows an error instead of an Authorize button**

The Authorization Callback Domain on Strava doesn't match where RunSync is running. Redo step 2.

**"Strava's rate limit for your API application has been reached"**

Strava allows a fixed number of requests per 15 minutes. Because you're on your own application,
this limit is yours alone — nobody else's syncing affects you. Wait 15 minutes and try again.

**My runs synced but aren't showing on the plan**

RunSync matches runs to plan days by the *local* date of the activity, and only imports
activities of type `Run`. A ride or a walk won't appear.

---

## Common questions

**Does this give RunSync access to my Strava password?**
No. You never give RunSync your Strava password. Strava's own authorization screen handles
sign-in, and RunSync only receives a token that can read your activities.

**What can RunSync actually see?**
Your activities, at the `activity:read_all` scope. It cannot post, edit, or delete anything on
Strava.

**Is my Client Secret safe?**
It's encrypted with AES-256-GCM before it's stored, and no part of RunSync's API will return it
once saved. It's decrypted only in memory, only when talking to Strava.

**How do I disconnect?**
Settings → **Remove** deletes your credentials and disconnects Strava. Your already-synced runs
are kept. You can also delete the application outright at
[strava.com/settings/api](https://www.strava.com/settings/api), which revokes access from
Strava's side.

**Will my friends see my runs in RunSync?**
No. Strava's API terms require that your activity data is shown only to you, and RunSync scopes
every query to the signed-in user.
