namespace NoCTF.Infrastructure.Runtime.Capacity;

internal static class RunnerAdmissionLua
{
    internal const string Functions = """
        local function track_claim(claim, runner)
            if string.match(claim, '^runner%-claim:%d+:') then
                local time = redis.call('TIME')
                redis.call('ZADD', 'runner:' .. runner .. ':unconfirmed-claims',
                    tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000), claim)
            end
        end
        local function ready(heartbeat, capacity)
            if redis.call('EXISTS', heartbeat) == 0 or redis.call('EXISTS', capacity) == 0 then return false end
            if redis.call('HGET', capacity, 'registrationSchema') ~= '3' then return false end
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
            local availableMemory = tonumber(redis.call('HGET', capacity, 'admissionAvailableMemoryBytes') or '-1')
            local availableCpu = tonumber(redis.call('HGET', capacity, 'admissionAvailableNanoCpus') or '-1')
            local availablePids = tonumber(redis.call('HGET', capacity, 'admissionAvailablePids') or '-1')
            local activeField = auxiliary and 'startingAuxiliary' or 'startingPrimary'
            local maxField = auxiliary and 'maxAuxiliary' or 'maxPrimary'
            local active = tonumber(redis.call('HGET', capacity, activeField) or '0')
            local maximum = tonumber(redis.call('HGET', capacity, maxField) or '0')
            if maximum > 0 and active >= maximum then return false end
            local pidsFit = redis.call('HGET', capacity, 'pidsObserved') == '0' or availablePids >= pids
            return availableMemory >= memory and availableCpu >= cpu and pidsFit
        end
        local function start_slot(capacity, claim, auxiliary, memory, cpu, pids)
            local field = auxiliary and 'startingAuxiliary' or 'startingPrimary'
            redis.call('HINCRBY', capacity, field, 1)
            redis.call('HSET', claim, 'starting', 1, 'auxiliary', auxiliary and 1 or 0,
                'acquiredObservedAt', redis.call('HGET', capacity, 'observationObservedAt') or '0')
            redis.call('HINCRBY', capacity, 'startupReservedMemoryBytes', memory)
            redis.call('HINCRBY', capacity, 'startupReservedNanoCpus', cpu)
            redis.call('HINCRBY', capacity, 'startupReservedPids', pids)
        end
        local function admission_pressure(capacityKey, jitter)
            local availableMemory = tonumber(redis.call('HGET', capacityKey, 'admissionAvailableMemoryBytes') or '0')
            local availableCpu = tonumber(redis.call('HGET', capacityKey, 'admissionAvailableNanoCpus') or '0')
            local availablePids = tonumber(redis.call('HGET', capacityKey, 'admissionAvailablePids') or '0')
            local totalMemory = tonumber(redis.call('HGET', capacityKey, 'observedTotalMemoryBytes') or '0')
            local totalCpu = tonumber(redis.call('HGET', capacityKey, 'observedTotalNanoCpus') or '0')
            local totalPids = tonumber(redis.call('HGET', capacityKey, 'observedTotalPids') or '0')
            if totalMemory <= 0 or totalCpu <= 0 then return 1 + jitter end
            local pressure = math.max(1 - (availableMemory / totalMemory), 1 - (availableCpu / totalCpu))
            if redis.call('HGET', capacityKey, 'pidsObserved') ~= '0' then
                if totalPids <= 0 then return 1 + jitter end
                pressure = math.max(pressure, 1 - (availablePids / totalPids))
            end
            return pressure + jitter
        end
        """;
}
