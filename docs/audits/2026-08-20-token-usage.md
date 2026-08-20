# Token-Verbrauch & Sparpotenzial (2026-08-20)

Auswertung des tatsächlichen Claude-Verbrauchs über alle Sessions dieses Accounts, plus
Scan dieses Repos auf die Faktoren, die den Verbrauch treiben. Grundlage sind die vom
Backend gemeldeten Session-Zähler (`cache_read`/`cache_write`/`input`/`output`/`cost_usd`),
nicht Schätzungen.

## 1. Was du im Schnitt verbrauchst

Vier Sessions liefern eine vollständige Token-Aufschlüsselung:

| Session | Modell | Effort | Tokens gesamt | Cache-Read | Output | Kosten |
|---|---|---|---:|---:|---:|---:|
| Vollständiger Code-Check | Opus 4.8 | max | 19.948.417 | 91,2 % | 425.329 | $56,17 |
| Analyse fix group movement | Opus 5 | max | 20.593.922 | 97,4 % | 99.949 | $15,27 |
| ED2K/AniDB/API auth (JDB) | Sonnet 5 | max | 81.761.716 | 97,5 % | 274.252 | $33,44 |
| Complete revision | Opus 5 | xhigh | 21.753.656 | 98,3 % | 47.406 | $32,54 |
| **Summe** | | | **144.057.711** | **96,8 %** | **846.936** | **$137,43** |

**Durchschnitt pro Session: ~36,0 Mio. Tokens / $34,36.**
Über alle acht Sessions mit Kostendaten (inkl. vier Fable-5-Sessions ohne Token-Split):
**$173,18 gesamt, Ø $21,65 pro Session.**

Die Zusammensetzung ist der eigentliche Befund:

| Posten | Ø pro Session | Anteil |
|---|---:|---:|
| Cache-Read (Kontext erneut lesen) | 34.848.150 | **96,8 %** |
| Cache-Write | 869.201 | 2,4 % |
| Input (ungecacht) | 85.343 | 0,2 % |
| Output (inkl. Thinking) | 211.734 | 0,6 % |

**97 % deines Verbrauchs ist nicht "neue Arbeit", sondern das Wiedereinlesen des bereits
angesammelten Kontexts.** Jeder einzelne Turn zahlt den kompletten bisherigen
Gesprächsverlauf noch einmal — zum Cache-Tarif (0,1× Input-Preis), aber eben bei
*jedem* Turn.

### Kostenmodell pro Turn (Opus 5, $5/MTok Input → $0,50/MTok Cache-Read)

| Kontextgröße | nur Cache-Read | + ~1,5k Output | pro Turn | 100 Turns |
|---:|---:|---:|---:|---:|
| 50.000 | $0,0250 | $0,0375 | $0,0625 | $6,25 |
| 100.000 | $0,0500 | $0,0375 | $0,0875 | $8,75 |
| 200.000 | $0,1000 | $0,0375 | $0,1375 | $13,75 |
| 400.000 | $0,2000 | $0,0375 | $0,2375 | $23,75 |
| 700.000 | $0,3500 | $0,0375 | $0,3875 | $38,75 |

Der Kontext ist damit kein Komfortthema, sondern ein Multiplikator: alles, was einmal im
Kontext landet, wird für den Rest der Session bei jedem Turn erneut bezahlt.

### Was die Zähler *nicht* zeigen

Rechnet man die Session-Zähler mit den Listenpreisen nach:

| Session | rekonstruiert | abgerechnet | Faktor |
|---|---:|---:|---:|
| Analyse fix group movement (Einzel-Thread) | $16,81 | $15,27 | 0,91× |
| ED2K/AniDB/API auth (Einzel-Thread) | $37,46 | $33,44 | 0,89× |
| Vollständiger Code-Check (Multi-Agent) | $32,93 | $56,17 | **1,71×** |
| Complete revision (viele Sub-Calls) | $15,17 | $32,54 | **2,15×** |

Einzel-Thread-Sessions stimmen auf ~10 % genau. Die beiden Multi-Agent-Sessions kosten
**1,7–2,2× mehr als ihre eigenen Token-Zähler ausweisen** — Subagenten werden abgerechnet,
tauchen in den Session-Zählern des Haupt-Threads aber nicht auf. Der Verbrauch von
Subagenten ist also systematisch höher als er sich anfühlt.

### Aktueller Limit-Status

Session `session_01Jo77…` (2026-08-20) meldet `rateLimitType: seven_day`,
`status: allowed_warning` — du läufst derzeit gegen das 7-Tage-Kontingent, nicht nur
gegen das 5-Stunden-Fenster.

## 2. Die Hebel — nach gemessener Wirkung sortiert

### H1 — Sessions kurz halten (größter Hebel, betrifft 96,8 % der Tokens)
Die teuerste Session dieses Repos (`ED2K/AniDB/API auth`) lief vom **30.06. bis 18.08.** —
sieben Wochen in einem Thread, 79,7 Mio. Cache-Reads, $33,44. Ein langer Thread wird mit
jedem Turn teurer, weil der Kontext monoton wächst.
→ **Pro Aufgabe eine neue Session.** Fertig = zumachen. Das ist der Unterschied zwischen
$0,06 und $0,24 pro Turn.

### H2 — Modellwahl (Faktor bis 3,3×)
| Modell | Input $/MTok | Output $/MTok | relativ zu Opus 5 |
|---|---:|---:|---:|
| Fable 5 | $10 | $50 | **2,0×** |
| Opus 5 | $5 | $25 | 1,0× |
| Sonnet 5 | $3 | $15 | 0,6× |
| Haiku 4.5 | $1 | $5 | 0,2× |

Du hast vier Sessions auf **Fable 5** laufen lassen ($35,75 zusammen) — das teuerste
verfügbare Modell, für Jellyfin-Plugin-Arbeit. Opus 5 reicht dafür; für Routine
(Versions-Bumps, Manifest-Pflege, Doku, Changelogs) reichen Sonnet 5 oder Haiku 4.5.
Fable 5 nur, wenn du bewusst das stärkste Modell willst.

### H3 — Effort-Level (treibt den teuersten Token-Typ)
Fast alle deine Sessions laufen auf `effort: max`. Output-Tokens kosten das 5-fache von
Input-Tokens ($25 vs. $5/MTok bei Opus 5), und `max` erzeugt am meisten davon (Thinking).
Aktuelle Empfehlung für Coding/agentische Arbeit ist `xhigh`; `high` ist der Sweet-Spot
aus Qualität und Tokenkosten. `max` gehört auf Aufgaben, bei denen Korrektheit teurer ist
als Tokens — nicht als Dauereinstellung.

### H4 — `CLAUDE.md` fehlt (30–60k Tokens pro Session, wiederkehrend)
`docs/BRAIN.md` (8,6 KB) ist inhaltlich exakt das, was eine Projekt-Memory sein soll —
Layout, Entscheidungen, Fallstricke. Sie liegt aber unter `docs/` und wird deshalb **nicht
automatisch geladen**. Ergebnis: jede Session erarbeitet sich dieselben Grundlagen neu
durch Lesen von Dateien.

Kosten der Alternative: das komplette Repo zu lesen sind **~233.000 Tokens**
(src ~144k, tests ~54k, docs ~23,5k). Realistische Exploration liegt bei 30–60k pro
Session — und wird danach bei jedem Turn erneut mitgelesen (H1).
→ `CLAUDE.md` im Repo-Root anlegen (Verweis auf oder Kurzfassung von BRAIN.md).
Nebenbefund: BRAIN.md steht auf *"current release: v0.1.8.2"*, aktuell ist **v0.1.9.8** —
veraltete Memory kostet zusätzlich Korrektur-Turns.

### H5 — Subagenten sparsam einsetzen (gemessen 1,7–2,2× Aufschlag)
Jeder Subagent startet kalt mit eigener Baseline (in dieser Session gemessen:
**46.954 Tokens** allein für System-Prompt + Tool-Schemas + Skill-Liste, bevor irgendetwas
passiert) und liest sich das nötige Kontextwissen neu an. Der teuerste Lauf überhaupt war
der Multi-Agent-Code-Check mit $56,17. Sinnvoll bei echtem Fan-out über viele Dateien —
nicht für Aufgaben, die ein Thread erledigt.

### H6 — Tool-Ausgaben klein halten
Eine einmal gelesene 6k-Token-Datei kostet über 100 Folge-Turns zusätzlich
6k × 100 × $0,50/MTok = **$0,30** — nur fürs Mitschleppen. Konkret in diesem Repo:

| Datei / Ordner | Größe | ~Tokens | Hinweis |
|---|---:|---:|---|
| `src/MetaHub.Infrastructure/Migrations/` (9 Dateien) | 139,6 KB | ~37.700 | EF-generiert, davon 5× `*.Designer.cs` |
| `docs/superpowers/plans/2026-06-05-…md` | 36,8 KB | ~9.950 | abgeschlossener Plan |
| `src/MetaHub.Jellyfin/Configuration/configPage.html` | 36,8 KB | ~9.940 | selten ganz nötig |
| `src/MetaHub.Jellyfin/Api/MetaHubBackend.cs` | 26,6 KB | ~7.190 | |
| `manifest.json` | 12,5 KB | ~3.390 | wächst mit jedem Release |

→ Gezielt `grep`/`sed -n 'X,Yp'` statt ganze Dateien; Migrations-Designer-Dateien nie
komplett lesen (autogeneriert, null Informationswert pro Token).

### H7 — Permission-Allowlist
Es gibt kein `.claude/settings.json` in diesem Repo. Jede abgelehnte oder rückgefragte
Tool-Ausführung kostet einen kompletten zusätzlichen Turn — und ein Turn heißt immer:
gesamter Kontext erneut gelesen. Eine Allowlist für die üblichen Read-only-Befehle
(`dotnet build`, `dotnet test`, `git status/log/diff`, `grep`, `ls`) amortisiert sich sofort.

## 3. Erwartete Wirkung

| Maßnahme | Aufwand | Wirkung |
|---|---|---|
| Neue Session pro Aufgabe (H1) | keiner | 40–70 % weniger Cache-Reads |
| Fable 5 → Opus 5 / Sonnet 5 (H2) | keiner | 50–70 % auf betroffenen Sessions |
| `effort: max` → `xhigh`/`high` (H3) | keiner | 20–40 % der Output-Kosten |
| `CLAUDE.md` anlegen (H4) | ~15 Min | 30–60k Tokens pro Session gespart |
| Subagenten nur bei Fan-out (H5) | keiner | vermeidet 1,7–2,2× Aufschlag |
| Große Dateien gezielt lesen (H6) | Gewohnheit | linear über alle Folge-Turns |
| `.claude/settings.json` (H7) | ~10 Min | spart Turns, nicht Tokens pro Turn |

H1 bis H3 kosten nichts außer einer Umstellung der Gewohnheit und adressieren zusammen
den weitaus größten Teil der gemessenen 36 Mio. Tokens pro Session.
