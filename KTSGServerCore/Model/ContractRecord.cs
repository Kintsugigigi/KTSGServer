namespace KTSG.Server.Model;

public class ContractRecord
{
    public string RoleId { get; set; } = string.Empty;

    public int RoleCid { get; set; }

    public int Score { get; set; }

    public List<int> Contacts { get; set; } = new();

    public DateTime RecordTime { get; set; }
}
