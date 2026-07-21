# ADR-0001: Compile-time game modes

Status: accepted

CTF, AWD, AWDP, KoH, and Penetration are compiled into `NoCTF.GameModes`. Competition mode is immutable, and each mode owns its strict JSON configuration, validator, and upgrader. Dynamic DLL loading, `AssemblyLoadContext`, `PluginBase`, generic `IGameMode`, and string action/view buses are superseded.
