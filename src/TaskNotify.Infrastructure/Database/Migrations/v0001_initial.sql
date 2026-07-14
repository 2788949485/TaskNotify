-- Phase 0 initial schema. Mirrors doc chapter 25.

CREATE TABLE DetectedTasks (
    Id TEXT PRIMARY KEY,
    Source TEXT NOT NULL,
    DisplayName TEXT NOT NULL,
    State INTEGER NOT NULL,
    Confidence INTEGER NOT NULL,
    ProbabilityScore INTEGER NOT NULL,
    RootProcessId INTEGER NULL,
    ProcessName TEXT NULL,
    CommandSummary TEXT NULL,
    WorkingDirectory TEXT NULL,
    DetectedAt TEXT NOT NULL,
    StartedAt TEXT NULL,
    EndedAt TEXT NULL,
    ExitCode INTEGER NULL,
    ResultMessage TEXT NULL,
    OpenPath TEXT NULL,
    LogPath TEXT NULL,
    CorrelationKey TEXT NULL,
    MetadataJson TEXT NOT NULL DEFAULT '{}'
);

CREATE INDEX IX_DetectedTasks_State ON DetectedTasks(State);
CREATE INDEX IX_DetectedTasks_EndedAt ON DetectedTasks(EndedAt);

CREATE TABLE ProcessEvents (
    Id TEXT PRIMARY KEY,
    TaskId TEXT NULL,
    ProcessId INTEGER NOT NULL,
    ParentProcessId INTEGER NULL,
    ProcessName TEXT NOT NULL,
    EventType TEXT NOT NULL,
    EventTime TEXT NOT NULL,
    FOREIGN KEY(TaskId) REFERENCES DetectedTasks(Id)
);

CREATE INDEX IX_ProcessEvents_TaskId ON ProcessEvents(TaskId);
CREATE INDEX IX_ProcessEvents_EventTime ON ProcessEvents(EventTime);

CREATE TABLE DetectionRules (
    Id TEXT PRIMARY KEY,
    RuleName TEXT NOT NULL,
    ProcessPattern TEXT NULL,
    CommandPattern TEXT NULL,
    ParentPattern TEXT NULL,
    MinimumDurationSeconds INTEGER NOT NULL DEFAULT 0,
    ScoreAdjustment INTEGER NOT NULL DEFAULT 0,
    Action INTEGER NOT NULL DEFAULT 0,
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    IsUserCreated INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE Integrations (
    Id TEXT PRIMARY KEY,
    IntegrationType TEXT NOT NULL UNIQUE,
    IsInstalled INTEGER NOT NULL DEFAULT 0,
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    Version TEXT NULL,
    ConfigPath TEXT NULL,
    LastCheckedAt TEXT NULL
);
