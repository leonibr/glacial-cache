

public static class LoggingIds
{
    public const int CacheInitialized = 1001;
    public const int CacheGetError = 1002;
    public const int CacheSetError = 1003;
    public const int CacheRemoveError = 1004;
    public const int CacheRefreshError = 1005;


    public const int CleanupExpiredItems = 1008;
    public const int CleanupExpiredItemsError = 1009;
    public const int InitializationError = 1010;

    // CleanupBackgroundService logging IDs
    public const int CleanupServiceStarted = 1039;
    public const int CleanupServiceStopping = 1040;
    public const int CleanupServiceSkipped = 1041;
    public const int CleanupServiceError = 1042;
    public const int CleanupServiceDisposed = 1043;
    public const int BatchSetSuccess = 1011;
    public const int BatchSetError = 1012;
    public const int BatchGetSuccess = 1013;
    public const int BatchGetError = 1014;
    public const int BatchRemoveSuccess = 1015;
    public const int BatchRemoveError = 1016;
    public const int BatchRefreshSuccess = 1017;
    public const int BatchRefreshError = 1018;
    public const int DeserializationError = 1019;
    public const int SerializationError = 1020;
    public const int LargeBatchProcessing = 1021;
    public const int ResilienceConnectionFailure = 1033;

    // Election State Operations
    public const int ElectionStateInitialized = 4007;
    public const int ElectionStateUpdated = 4008;
    public const int ElectionStateConcurrentAccess = 4009;

    // Election Background Service Operations
    public const int ElectionServiceStarted = 4010;
    public const int ElectionServiceStopped = 4011;
    public const int ElectionLeadershipAcquired = 4012;
    public const int ElectionLeadershipLost = 4013;
    public const int ElectionVoluntaryYield = 4014;
    public const int ElectionBackoffAttempt = 4015;
    public const int ElectionAdvisoryLockAcquired = 4016;
    public const int ElectionAdvisoryLockFailed = 4017;
    public const int ElectionLockVerification = 4018;
    public const int ElectionServiceError = 4019;
    public const int ElectionServiceDisposed = 4020;

    // Configuration Change Detection Operations (5000-5999)
    public const int ConfigurationPropertyChanged = 5001;
    public const int ConfigurationPropertyRegistered = 5002;
    public const int ConfigurationPropertyUnregistered = 5003;
    public const int ConfigurationSyncCompleted = 5004;
    public const int ConfigurationSyncError = 5005;
    public const int ObservablePropertyError = 5006;
    public const int ObservablePropertyThreadSafetyWarning = 5007;
}