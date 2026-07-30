import { describe, expect, test } from 'bun:test'
import { createI18n } from 'vue-i18n'
import {
  buildCompetitionChallengeCreateRequest,
  buildCompetitionChallengeUpdateRequest,
  isAvailableCompetitionChallengeTemplate,
  isDeletedCompetitionChallenge,
  isValidOptionalCompetitionChallengeId,
  nextCompetitionChallengeOrder,
} from '../src/components/admin/competition-detail/competitionChallengeLifecycle'
import en from '../src/locales/en.json'
import zhCN from '../src/locales/zh-CN.json'

const stableId = '00112233-4455-6677-8899-aabbccddeeff'

describe('competition challenge lifecycle', () => {
  test('builds the current create contract and preserves an explicit stable ID', () => {
    expect(
      buildCompetitionChallengeCreateRequest({
        stableId: ` ${stableId} `,
        challengeId: 'template-01',
        baseScore: 500,
        order: 3,
        isPublished: true,
      }),
    ).toEqual({
      id: stableId,
      challengeId: 'template-01',
      baseScore: 500,
      order: 3,
    })

    expect(
      buildCompetitionChallengeCreateRequest({
        stableId: '',
        challengeId: 'template-01',
        baseScore: 500,
        order: 3,
        isPublished: true,
      }),
    ).toEqual({
      challengeId: 'template-01',
      baseScore: 500,
      order: 3,
    })
  })

  test('rejects invalid IDs and invalid bounded numbers', () => {
    expect(isValidOptionalCompetitionChallengeId('')).toBe(true)
    expect(isValidOptionalCompetitionChallengeId(stableId)).toBe(true)
    expect(
      isValidOptionalCompetitionChallengeId('00000000-0000-0000-0000-000000000000'),
    ).toBe(false)
    expect(isValidOptionalCompetitionChallengeId('not-a-guid')).toBe(false)

    expect(
      buildCompetitionChallengeCreateRequest({
        stableId: '',
        challengeId: 'template-01',
        baseScore: -1,
        order: 0,
        isPublished: false,
      }),
    ).toBeNull()
    expect(
      buildCompetitionChallengeCreateRequest({
        stableId: '',
        challengeId: 'template-01',
        baseScore: 100,
        order: 1.5,
        isPublished: false,
      }),
    ).toBeNull()
  })

  test('fails closed when the selected template is no longer available', () => {
    const templates = [{ id: 'template-01' }, { id: 'template-02' }]

    expect(isAvailableCompetitionChallengeTemplate('template-01', templates)).toBe(true)
    expect(isAvailableCompetitionChallengeTemplate('deleted-template', templates)).toBe(false)
    expect(isAvailableCompetitionChallengeTemplate('', templates)).toBe(false)
  })

  test('uses the latest response revision and fails closed for deleted or incomplete data', () => {
    const draft = {
      stableId,
      challengeId: 'template-01',
      baseScore: 750,
      order: 4,
      isPublished: true,
    }
    const challenge = {
      id: stableId,
      challengeId: 'template-01',
      revision: 8,
      deletedAt: null,
    }

    expect(buildCompetitionChallengeUpdateRequest(challenge, draft)).toEqual({
      baseScore: 750,
      order: 4,
      isPublished: true,
      expectedRevision: 8,
    })
    expect(
      buildCompetitionChallengeUpdateRequest({ ...challenge, revision: undefined }, draft),
    ).toBeNull()
    expect(
      buildCompetitionChallengeUpdateRequest(
        { ...challenge, deletedAt: '2026-07-31T00:00:00Z' },
        draft,
      ),
    ).toBeNull()
  })

  test('maps deletion state and chooses the next active order', () => {
    const active = { order: 2, deletedAt: null }
    const deleted = { order: 9, deletedAt: '2026-07-31T00:00:00Z' }

    expect(isDeletedCompetitionChallenge(active)).toBe(false)
    expect(isDeletedCompetitionChallenge(deleted)).toBe(true)
    expect(nextCompetitionChallengeOrder([active, deleted, { order: 5 }])).toBe(6)
  })

  test.each([
    ['en', en],
    ['zh-CN', zhCN],
  ])('provides lifecycle copy for %s', (locale, messages) => {
    const i18n = createI18n({
      legacy: false,
      locale,
      messages: { [locale]: messages },
    })

    for (const key of [
      'admin.competitionDetail.stableChallengeId',
      'admin.competitionDetail.includeDeletedChallenges',
      'admin.competitionDetail.challengeRevision',
      'admin.competitionDetail.restoreChallenge',
      'admin.competitionDetail.challengeRevisionConflict',
      'admin.competitionDetail.challengeLifecycleConflict',
      'admin.competitionDetail.newChallengeStartsDraft',
    ]) {
      expect(i18n.global.t(key)).not.toBe(key)
    }
  })
})
