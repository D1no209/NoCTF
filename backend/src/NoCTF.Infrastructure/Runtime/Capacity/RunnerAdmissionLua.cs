namespace NoCTF.Infrastructure.Runtime.Capacity;

internal static class RunnerAdmissionLua
{
    internal const string Functions = """
        local function ready(heartbeat, capacity)
            if redis.call('EXISTS', heartbeat) == 0 or redis.call('EXISTS', capacity) == 0 then return false end
            local state = redis.call('HGET', capacity, 'admissionState')
            if state and state ~= 'ready' then return false end
            if redis.call('HGET', capacity, 'observationRequired') == '1' then
                local time = redis.call('TIME')
                local now = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)
                local untilAt = tonumber(redis.call('HGET', capacity, 'observationFreshUntil') or '0')
                if untilAt < now then return false end
            end
            return true
        end
        local function fits(capacity, memory, cpu, pids, auxiliary)
            local availableMemory = tonumber(redis.call('HGET', capacity, 'availableMemoryBytes') or '-1')
            local availableCpu = tonumber(redis.call('HGET', capacity, 'availableNanoCpus') or '-1')
            local availablePids = tonumber(redis.call('HGET', capacity, 'availablePids') or '-1')
            local activeField = auxiliary and 'activeAuxiliary' or 'startingPrimary'
            local maxField = auxiliary and 'maxAuxiliary' or 'maxPrimary'
            local active = tonumber(redis.call('HGET', capacity, activeField) or '0')
            local maximum = tonumber(redis.call('HGET', capacity, maxField) or '0')
            if maximum > 0 and active >= maximum then return false end
            if not auxiliary then
                availableMemory = availableMemory - tonumber(redis.call('HGET', capacity, 'reservedMemory') or '0')
                availableCpu = availableCpu - tonumber(redis.call('HGET', capacity, 'reservedCpu') or '0')
                availablePids = availablePids - tonumber(redis.call('HGET', capacity, 'reservedPids') or '0')
            end
            return availableMemory >= memory and availableCpu >= cpu and availablePids >= pids
        end
        local function start_slot(capacity, claim, auxiliary)
            local field = auxiliary and 'activeAuxiliary' or 'startingPrimary'
            redis.call('HINCRBY', capacity, field, 1)
            redis.call('HSET', claim, 'starting', 1, 'auxiliary', auxiliary and 1 or 0)
        end
        """;
}
