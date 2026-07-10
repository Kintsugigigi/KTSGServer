namespace KTSG.Server.Model;

public class ContractReady
{
    public int Score { get; set; }

    public List<int> Contacts { get; set; } = new();
}
