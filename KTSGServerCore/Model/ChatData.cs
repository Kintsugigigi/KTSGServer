using KTSG.Proto;
using KTSG.Server.Config;
using MongoDB.Bson.Serialization.Attributes;

namespace KTSG.Server.Model;

[BsonIgnoreExtraElements]
public sealed class ChatData
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    public string SenderUid { get; set; } = string.Empty;

    public string ReceiverUid { get; set; } = string.Empty;

    public long UnixTimeMs { get; set; }

    public string Text { get; set; } = string.Empty;

    public NChatMsg ToNChatMsg()
    {
        return new NChatMsg
        {
            SenderUid = SenderUid,
            ReceiverUid = ReceiverUid,
            UnixTimeMs = UnixTimeMs,
            Text = Text
        };
    }
}
