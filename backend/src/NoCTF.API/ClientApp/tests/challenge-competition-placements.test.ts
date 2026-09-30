import { expect, test } from 'bun:test'
import { availablePlacementCompetitions, placementManagementPath, projectChallengePlacements, writablePlacementCompetitions } from '../app/features/admin/challenge-competition-placements'
import { useContentSwap } from '../app/motion/useContentSwap'
import { ref } from 'vue'

test('placements fail closed for absent permissions, read-only roles and deleted competitions', () => {
  const writable = writablePlacementCompetitions([
    { id: 'owner', administrationRole: 'Owner' },
    { id: 'manager', administrationRole: 'Manager' },
    { id: 'judge', administrationRole: 'Judge' },
    { id: 'observer', administrationRole: 'Observer' },
    { id: 'unknown', administrationRole: null },
    { id: 'deleted', administrationRole: 'Owner', deletedAt: '2026-09-30' },
  ])
  expect(writable.map(item => item.id)).toEqual(['owner', 'manager'])
})

test('active placements match template identity and append after the last active challenge', () => {
  const placement = projectChallengePlacements({ id: 'competition', mode: 'Ctf' }, [
    { id: 'same-title', challengeId: 'other', title: 'Shared title', order: 5 },
    { id: 'correct', challengeId: 'template', order: 2 },
    { id: 'deleted', challengeId: 'template', deletedAt: '2026-09-30', order: 99 },
  ], 'template')
  expect(placement.instances.map(item => item.id)).toEqual(['correct'])
  expect(placement.nextOrder).toBe(6)
  expect(placementManagementPath(placement)).toBe('/admin/competitions/competition/challenges/correct')
})

test('movie roll changes direction when selecting a previous competition', () => {
  const direction = ref<'film-left' | 'film-right'>('film-left')
  const motion = useContentSwap(direction)
  expect(motion.transition.name).toBe('noctf-film-left')
  direction.value = 'film-right'
  expect(motion.transition.name).toBe('noctf-film-right')
  expect(motion.transition.mode).toBe('out-in')
})

test('quick add excludes existing placements and other modes', () => {
  const placed = projectChallengePlacements({ id: 'placed', mode: 'Ctf' }, [{ challengeId: 'template' }], 'template')
  const other = projectChallengePlacements({ id: 'other-mode', mode: 'Awd' }, [], 'template')
  const free = projectChallengePlacements({ id: 'available', mode: 'Ctf' }, [], 'template')
  expect(availablePlacementCompetitions([placed, other, free], 'Ctf').map(item => item.competition.id)).toEqual(['available'])
})
