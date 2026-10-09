using NoCTF.Infrastructure.LiveSolo.Media;

namespace NoCTF.Tests.Unit.LiveSolo;

public sealed class LiveSoloCaptureFilesTests
{
    [Test]
    public async Task Recording_size_observes_the_current_file_and_cleanup_removes_only_its_capture_directory()
    {
        var root=Path.Combine(Path.GetTempPath(),"noctf-capture-test-"+Guid.NewGuid().ToString("N"));
        var id=Guid.NewGuid();var folder=Path.Combine(root,"live-solo",id.ToString("N"));Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"recording.mp4");var files=new LiveSoloCaptureFiles(new() {CaptureSpoolPath=root});
        try
        {
            await Assert.That(await files.RecordingLengthAsync(id,null,CancellationToken.None)).IsNull();
            await File.WriteAllBytesAsync(path,[1,2,3,4]);
            await Assert.That(await files.RecordingLengthAsync(id,null,CancellationToken.None)).IsEqualTo(4L);
            await File.WriteAllBytesAsync(path,[1,2,3,4,5,6]);
            await Assert.That(await files.RecordingLengthAsync(id,null,CancellationToken.None)).IsEqualTo(6L);
            await files.RemoveAsync(id,CancellationToken.None);
            await Assert.That(Directory.Exists(folder)).IsFalse();
            await Assert.That(Directory.Exists(root)).IsTrue();
        }
        finally { if(File.Exists(path))File.Delete(path);if(Directory.Exists(folder))Directory.Delete(folder);
            Directory.Delete(Path.Combine(root,"live-solo"));Directory.Delete(root); }
    }
    [Test]
    public async Task Playlist_parser_retains_segment_metadata_after_raw_cleanup_and_rejects_traversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "noctf-capture-test-" + Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid(); var folder = Path.Combine(root, "live-solo", id.ToString("N"));
        Directory.CreateDirectory(folder);
        var playlist = Path.Combine(folder, "program.m3u8"); var segment = Path.Combine(folder, "program_00000.ts");
        try
        {
            await File.WriteAllBytesAsync(segment, [0x47, 1, 2, 3]);
            await File.WriteAllTextAsync(playlist, "#EXTM3U\n#EXTINF:2.000,\nprogram_00000.ts\n");
            var files = new LiveSoloCaptureFiles(new() { CaptureSpoolPath = root });
            var rows = await files.SegmentsAsync(id, CancellationToken.None);
            await Assert.That(rows.Count).IsEqualTo(1); await Assert.That(rows[0].Sequence).IsEqualTo(0L);
            await using (var stream = await files.OpenAsync(id, rows[0].FileName, CancellationToken.None))
                await Assert.That(stream!.Length).IsEqualTo(4L);
            await files.RemoveSegmentAsync(id,rows[0].FileName,CancellationToken.None);
            await Assert.That((await files.SegmentsAsync(id,CancellationToken.None)).Single().Duration).IsEqualTo(TimeSpan.FromSeconds(2));
            await Assert.That(await files.OpenAsync(id,rows[0].FileName,CancellationToken.None)).IsNull();
            await File.WriteAllTextAsync(playlist, "#EXTM3U\n#EXTINF:2.000,\n../../secret.ts\n");
            await Assert.That(async () => await files.SegmentsAsync(id, CancellationToken.None)).Throws<InvalidDataException>();
            await Assert.That(async () => await files.OpenAsync(id, "../recording.mp4", CancellationToken.None)).Throws<InvalidDataException>();
        }
        finally
        {
            File.Delete(playlist); File.Delete(segment); Directory.Delete(folder); Directory.Delete(Path.Combine(root, "live-solo")); Directory.Delete(root);
        }
    }
    [Test]
    public async Task Active_recording_size_reads_provider_staging_and_cleanup_cannot_touch_a_sibling_or_unexpected_file()
    {
        var root=Path.Combine(Path.GetTempPath(),"noctf-capture-test-"+Guid.NewGuid().ToString("N"));
        var parent=Path.Combine(root,".egress-tmp");var current=Path.Combine(parent,"EG_current");var sibling=Path.Combine(parent,"EG_sibling");
        Directory.CreateDirectory(current);Directory.CreateDirectory(sibling);
        var path=Path.Combine(current,"recording.mp4");var other=Path.Combine(sibling,"recording.mp4");var unexpected=Path.Combine(current,"inspect.txt");
        var files=new LiveSoloCaptureFiles(new(){CaptureSpoolPath=root});var id=Guid.NewGuid();
        try
        {
            await Assert.That(await files.RecordingLengthAsync(id,"EG_current",CancellationToken.None)).IsNull();
            await File.WriteAllBytesAsync(path,[1,2,3,4]);await File.WriteAllBytesAsync(other,[5,6]);
            await Assert.That(await files.RecordingLengthAsync(id,"EG_current",CancellationToken.None)).IsEqualTo(4L);
            await File.WriteAllBytesAsync(path,[1,2,3,4,5,6]);
            await Assert.That(await files.RecordingLengthAsync(id,"EG_current",CancellationToken.None)).IsEqualTo(6L);
            await Assert.That(async()=>await files.RecordingLengthAsync(id,"../EG_sibling",CancellationToken.None)).Throws<InvalidDataException>();
            await Assert.That(async()=>await files.RemoveRecordingStagingAsync("EG_current/..",CancellationToken.None)).Throws<InvalidDataException>();
            await File.WriteAllTextAsync(unexpected,"requires inspection");
            await Assert.That(async()=>await files.RemoveRecordingStagingAsync("EG_current",CancellationToken.None)).Throws<IOException>();
            await Assert.That(File.Exists(unexpected)).IsTrue();await Assert.That(File.Exists(other)).IsTrue();
            File.Delete(unexpected);await files.RemoveRecordingStagingAsync("EG_current",CancellationToken.None);
            await Assert.That(Directory.Exists(current)).IsFalse();await Assert.That(File.Exists(other)).IsTrue();
        }
        finally
        {
            if(File.Exists(path))File.Delete(path);if(File.Exists(unexpected))File.Delete(unexpected);File.Delete(other);
            if(Directory.Exists(current))Directory.Delete(current);Directory.Delete(sibling);Directory.Delete(parent);Directory.Delete(root);
        }
    }
}
