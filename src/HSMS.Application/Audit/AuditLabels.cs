namespace HSMS.Application.Audit;

/// <summary>Stable module names for filtering and dashboards (no PHI in the label itself).</summary>
public static class AuditModules
{
    public const string Security = "Security";
    public const string Sterilization = "Sterilization";
    public const string Masters = "Masters";
}

/// <summary>Structured action identifiers: {Area}.{Verb} or {Area}.{Sub}.{Verb}</summary>
public static class AuditEntities
{
    public const string LoginAttempt = "login_attempt";
    public const string Account = "tbl_account_login";
}

public static class AuditActions
{
    public const string LoginSuccess = "Login.Success";
    public const string LoginFailed = "Login.Failed";
    public const string LoginInactive = "Login.InactiveAccount";
    public const string AlertMultipleFailedLogins = "Alert.MultipleFailedLogins";
    public const string AccountCreate = "Account.Create";
    public const string ProfileUpdate = "Profile.Update";
    public const string SterilizationCreate = "Sterilization.Create";
    public const string SterilizationUpdate = "Sterilization.Update";
    public const string MastersSterilizerDeactivate = "Masters.Sterilizer.Deactivate";
    public const string MastersSterilizerUpdate = "Masters.Sterilizer.Update";
    public const string MastersDepartmentDeactivate = "Masters.Department.Deactivate";
    public const string MastersDepartmentUpdate = "Masters.Department.Update";
    public const string MastersDeptItemDeactivate = "Masters.DeptItem.Deactivate";
    public const string MastersDeptItemUpdate = "Masters.DeptItem.Update";
    public const string MastersDoctorRoomDeactivate = "Masters.DoctorRoom.Deactivate";
    public const string MastersDoctorRoomUpdate = "Masters.DoctorRoom.Update";
}
