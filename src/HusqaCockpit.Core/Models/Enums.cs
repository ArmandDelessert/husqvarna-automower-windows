namespace HusqaCockpit.Core.Models;

// The API sends these values in UPPER_SNAKE_CASE (e.g. "PARKED_IN_CS").
// Unknown values sent by newer API versions are mapped to the first member (Unknown)
// by TolerantEnumConverterFactory instead of failing the whole payload.

public enum MowerActivity
{
    Unknown,
    NotApplicable,
    Mowing,
    GoingHome,
    Charging,
    Leaving,
    ParkedInCs,
    StoppedInGarden,
}

public enum MowerState
{
    Unknown,
    NotApplicable,
    Paused,
    InOperation,
    WaitUpdating,
    WaitPowerUp,
    Restricted,
    Off,
    Stopped,
    Error,
    FatalError,
    ErrorAtPowerUp,
}

public enum MowerMode
{
    Unknown,
    MainArea,
    SecondaryArea,
    Home,
    Demo,
    Poi,
}

public enum InactiveReason
{
    Unknown,
    None,
    Planning,
    SearchingForSatellites,
}

public enum RestrictedReason
{
    Unknown,
    None,
    WeekSchedule,
    ParkOverride,
    Sensor,
    DailyLimit,
    Fota,
    Frost,
    AllWorkAreasCompleted,
    External,
    NotApplicable,
}

public enum OverrideAction
{
    Unknown,
    NotActive,
    ForcePark,
    ForceMow,
}

public enum HeadlightMode
{
    Unknown,
    AlwaysOn,
    AlwaysOff,
    EveningOnly,
    EveningAndNight,
}

public enum MessageSeverity
{
    Unknown,
    Fatal,
    Error,
    Warning,
    Info,
    Debug,
    Sw,
}
