using ShiftClub.Shared.Licensing;

namespace ShiftClub.Application.Tests;

public class InstallerFileStoreTests
{
    [Fact]
    public async Task Save_then_read_roundtrip_and_reject_bad_extension()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shift-dl-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var store = new InstallerFileStore(root);
            await using var body = new MemoryStream("MZ-setup"u8.ToArray());
            var error = await store.SaveAsync(InstallerFileStore.ServerSlot, "Setup Server.exe", body, CancellationToken.None);
            Assert.Null(error);

            var published = store.Read(InstallerFileStore.ServerSlot);
            Assert.NotNull(published);
            Assert.Equal("Setup_Server.exe", published!.FileName);
            Assert.Equal(8, published.Size);
            Assert.Equal("MZ-setup", File.ReadAllText(published.AbsolutePath));

            await using var bad = new MemoryStream("nope"u8.ToArray());
            var rejected = await store.SaveAsync(InstallerFileStore.ClientSlot, "notes.txt", bad, CancellationToken.None);
            Assert.NotNull(rejected);
            Assert.Null(store.Read(InstallerFileStore.ClientSlot));
            Assert.NotNull(store.Read(InstallerFileStore.ServerSlot));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsSlot_accepts_only_server_and_client()
    {
        Assert.True(InstallerFileStore.IsSlot("server"));
        Assert.True(InstallerFileStore.IsSlot("client"));
        Assert.False(InstallerFileStore.IsSlot("../server"));
        Assert.False(InstallerFileStore.IsSlot(""));
    }
}
