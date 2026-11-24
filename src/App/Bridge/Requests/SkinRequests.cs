using AxoClient.DataModel.Accounts;
using AxoClient.DataModel.Skins;

namespace AxoClient.App.Bridge.Requests
{
    public sealed record SkinAddData(string? FileRef, string? Base64, string Name, SkinModel Model);

    public sealed record SkinAdoptData(string Player);

    public sealed record SkinUpdateData(string Id, string? Name, SkinModel? Model, string? Base64);

    public sealed record MojangCapeData(string? CapeId);

    public sealed record CapeSaveData(string? Id, string Name, string Base64, CapeSource Source, CapeEditState? Edit);

    public sealed record AxoCapeData(AxoCapeSource? Source, string? Id);

    public sealed record ReviewData(string Id, bool Approve, string? Reason);

    public sealed record OfficialCapeData(string Name, string Base64);

    public sealed record AdminData(string Uuid, bool Admin);
}
