using System.Diagnostics;
using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class LiveSoloMediaDeploymentTests
{
    [Test]
    public async Task Optional_media_overlay_renders_private_control_ports_separate_redis_and_only_explicit_spool_mounts()
    {
        var repository=Repository();var source=Path.Combine(repository,"deploy","docker");
        var root=Path.Combine(Path.GetTempPath(),"noctf-media-compose-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var values=new Dictionary<string,string> {
                ["NOCTF_PLATFORM_IMAGE"]="ghcr.io/d1no209/noctf@sha256:"+new string('1',64),["POSTGRES_PASSWORD"]=new string('p',32),
                ["JWT_SECRET"]=new string('j',64),["RUNNER_SCORING_SECRET"]=new string('r',64),["REGISTRY_HTTP_SECRET"]=new string('s',64),
                ["EMAIL_VERIFICATION_ENCRYPTION_KEY"]=Convert.ToBase64String(new byte[32]),["SEED_ADMIN_EMAIL"]="admin@example.test",
                ["SEED_ADMIN_PASSWORD"]="test-password-1234",["NOCTF_PUBLIC_HOST"]="noctf.example.test",["NOCTF_PUBLIC_URL"]="https://noctf.example.test",
                ["NOCTF_PROXY_NETWORK"]="172.20.0.0/16",["DOCKER_PUBLISHED_HOST"]="challenges.example.test"};
            await File.WriteAllLinesAsync(Path.Combine(root,".env"),(await File.ReadAllLinesAsync(Path.Combine(source,".env.example"))).Select(line=>{
                var equals=line.IndexOf('=');return equals>0&&values.TryGetValue(line[..equals],out var value)?line[..(equals+1)]+value:line;
            }).Concat(new[]{"LIVE_SOLO_SPOOL_PATH=/bounded/media","LIVE_SOLO_MEDIA_GID=2001"}));
            foreach(var service in new[]{"noctf","postgres","registry"}){var dir=Path.Combine(root,"env",service);Directory.CreateDirectory(dir);
                File.Copy(Path.Combine(source,"env",service,".env.example"),Path.Combine(dir,".env"));}
            Directory.CreateDirectory(Path.Combine(root,"env","live-solo"));await File.WriteAllTextAsync(Path.Combine(root,"env","live-solo","noctf.env"),"LiveSolo__Media__Enabled=true\n");
            var start=new ProcessStartInfo("docker"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
            foreach(var argument in new[]{"compose","--project-directory",root,"--env-file",Path.Combine(root,".env"),"-f",Path.Combine(source,"docker-compose.yml"),
                "-f",Path.Combine(source,"compose.live-solo.yml"),"config","--format","json"})start.ArgumentList.Add(argument);
            using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();await Assert.That(process.ExitCode).IsEqualTo(0);_ = await error;
            using var json=JsonDocument.Parse(await output);var services=json.RootElement.GetProperty("services");
            foreach(var service in new[]{"media-redis","media-egress"})
            {
                var item=services.GetProperty(service);await Assert.That(item.TryGetProperty("ports",out _)).IsFalse();
                await Assert.That(item.GetProperty("networks").EnumerateObject().Select(x=>x.Name)).IsEquivalentTo(new[]{"media"});
            }
            var sfu=services.GetProperty("media-sfu");
            await Assert.That(sfu.GetProperty("ports").EnumerateArray().Select(x=>x.GetProperty("target").GetInt32())).IsEquivalentTo(new[]{7881,7882});
            await Assert.That(sfu.GetProperty("networks").TryGetProperty("challenges",out _)).IsFalse();
            var egress=services.GetProperty("media-egress");
            await Assert.That(egress.GetProperty("volumes").EnumerateArray().Single(x=>x.GetProperty("target").GetString()=="/out").GetProperty("source").GetString()).IsEqualTo("/bounded/media");
            await Assert.That(egress.GetProperty("group_add")[0].GetString()).IsEqualTo("2001");
            var staging=egress.GetProperty("volumes").EnumerateArray().Single(x=>x.GetProperty("target").GetString()=="/home/egress/tmp");
            await Assert.That(staging.GetProperty("source").GetString()).IsEqualTo("/bounded/media/.egress-tmp");
            await Assert.That(staging.GetProperty("bind").GetProperty("create_host_path").GetBoolean()).IsFalse();
            var proxy=await File.ReadAllTextAsync(Path.Combine(source,"live-solo","nginx.conf"));
            await Assert.That(proxy).Contains("location = /rtc");await Assert.That(proxy).Contains("return 404;");await Assert.That(proxy).DoesNotContain("$request_uri");
            await Assert.That(await File.ReadAllTextAsync(Path.Combine(source,"docker-compose.yml"))).DoesNotContain("media-sfu");
        }
        finally
        {
            var resolved=Path.GetFullPath(root);var intended=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"noctf-media-compose-"));
            if(!resolved.StartsWith(intended,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Invalid test directory.");
            Directory.Delete(resolved,true);
        }
    }
    private static string Repository()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"deploy","docker","docker-compose.yml")))directory=directory.Parent;
        return directory?.FullName??throw new DirectoryNotFoundException();
    }
}
