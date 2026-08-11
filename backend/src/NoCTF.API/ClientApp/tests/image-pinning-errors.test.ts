import { describe, expect, test } from 'bun:test'
import {
  imagePinningErrorMessage,
  isRevisionConflict,
  startGateErrorMessage,
} from '../app/utils/admin-format'

describe('container image publication failures', () => {
  test('maps stable registry failures without treating every 409 as a revision conflict', () => {
    const error = {
      status: 409,
      code: 'RegistryUnavailable',
      detail: 'Registry is unavailable.',
    }

    expect(imagePinningErrorMessage(error)).toBe('无法连接镜像仓库,请稍后重试')
    expect(isRevisionConflict(error)).toBeFalse()
  })

  test('keeps challenge definition revision conflicts distinct', () => {
    expect(isRevisionConflict({
      status: 409,
      code: 'ChallengeDefinitionRevisionConflict',
      detail: 'Challenge changed concurrently.',
    })).toBeTrue()
  })

  test('private registry guidance offers immutable digest input without claiming credentials exist', () => {
    expect(imagePinningErrorMessage({
      status: 409,
      code: 'RegistryAuthenticationRequired',
    })).toContain('image@sha256 digest')
  })

  test('localizes the digest start-gate failure', () => {
    expect(startGateErrorMessage({
      code: 'RuntimeImageNotPinned',
      message: 'Every image must use a digest.',
    })).toBe('所有运行时和 Checker 镜像必须固定为 sha256 digest 后才能开始比赛')
  })
})
