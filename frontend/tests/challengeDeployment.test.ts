import { describe, expect, it } from 'bun:test'
import { hasContainerDeployment } from '../src/lib/challengeDeployment'

describe('challenge deployment presentation', () => {
  it('shows a container mode only for container-backed assets', () => {
    expect(hasContainerDeployment('NoAttachment')).toBeFalse()
    expect(hasContainerDeployment('StaticAttachment')).toBeFalse()
    expect(hasContainerDeployment('DynamicContainer')).toBeTrue()
    expect(hasContainerDeployment('StaticContainer')).toBeTrue()
    expect(hasContainerDeployment(0)).toBeFalse()
    expect(hasContainerDeployment(2)).toBeTrue()
  })
})
