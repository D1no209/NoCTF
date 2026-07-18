export const challengeDeploymentTypes = [
  'NoAttachment',
  'StaticAttachment',
  'DynamicContainer',
  'StaticContainer',
] as const

export type ChallengeDeploymentType = typeof challengeDeploymentTypes[number] | number

export function challengeDeploymentTypeKey(value: ChallengeDeploymentType) {
  return typeof value === 'number'
    ? challengeDeploymentTypes[value] ?? 'NoAttachment'
    : value
}

export function hasContainerDeployment(value: ChallengeDeploymentType) {
  const deploymentType = challengeDeploymentTypeKey(value)
  return deploymentType === 'DynamicContainer' || deploymentType === 'StaticContainer'
}
