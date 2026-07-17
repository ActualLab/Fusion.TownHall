using ActualLab.Serialization;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.SignalR;

namespace TownHall.UI.Services;

// The SignalR MessagePack protocol config, shared by the server (hub) and the WASM client so both wire
// bytes the same way. MessagePackByteSerializer.DefaultResolver is ActualLab's resolver (handles Moment
// via its [MessagePackFormatter], ImmutableArray, etc.); ContractlessStandardResolver then serializes
// our plain contract/command records by member name - so they need no MessagePack attributes.
public static class HubProtocolConfig
{
    public static readonly IFormatterResolver Resolver = CompositeResolver.Create(
        MessagePackByteSerializer.DefaultResolver,
        ContractlessStandardResolver.Instance);

    public static void Configure(MessagePackHubProtocolOptions options)
        => options.SerializerOptions = MessagePackSerializerOptions.Standard
            .WithResolver(Resolver)
            .WithSecurity(MessagePackSecurity.UntrustedData);
}
