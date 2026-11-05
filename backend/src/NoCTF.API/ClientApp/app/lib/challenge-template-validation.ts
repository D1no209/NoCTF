import type { DefinitionModel, RunnerJobModel } from '../utils/game-config'
import { HARD_MAXIMUM_PATCH_UPLOAD_BYTES } from '../utils/game-config'
import { translate } from '../utils/i18n'

const environmentNamePattern = /^[A-Za-z_][A-Za-z0-9_]*$/
export interface ChallengeTemplateDraft { title: string; direction: string; definition: DefinitionModel }

function validPort(value: number | null | undefined): value is number {
  return value != null && Number.isInteger(value) && value >= 1 && value <= 65535
}
function validateEnvironment(issues: string[], environment: Record<string, string>, label: string) {
  for (const name of Object.keys(environment)) {
    if (!environmentNamePattern.test(name)) issues.push(translate('challenges.challengeTemplate.error.environmentVariableNameInvalid', { label, name }))
    if (name.toUpperCase().startsWith('NOCTF_')) issues.push(translate('challenges.challengeTemplate.validation.environmentVariablesFormat', { label }))
  }
}
function validateChecker(issues: string[], job: RunnerJobModel | undefined) {
  const label = translate('challenges.checker.label')
  if (!job || !job.image.trim()) { issues.push(translate('challenges.validation.imageRequired', { label })); return }
  if (job.image.length > 512) issues.push(translate('challenges.validation.imageExceedLength', { label }))
  if (job.command.some(argument => !argument.trim())) issues.push(translate('challenges.challengeTemplate.validation.commandBlankFormat', { label }))
  if (job.timeoutSeconds !== null && (!Number.isInteger(job.timeoutSeconds) || job.timeoutSeconds < 1 || job.timeoutSeconds > 1800))
    issues.push(translate('challenges.challengeTemplate.validation.timeoutSecondsRange', { label }))
  validateEnvironment(issues, job.environment, label)
}

export function validateChallengeTemplateDraft(draft: ChallengeTemplateDraft): string[] {
  const issues: string[] = []
  if (!draft.title.trim()) issues.push(translate('challenges.validation.titleRequired'))
  else if (draft.title.trim().length > 160) issues.push(translate('challenges.validation.titleLength'))
  if (!draft.direction.trim()) issues.push(translate('challenges.validation.directionRequired'))
  else if (draft.direction.trim().length > 96) issues.push(translate('challenges.validation.directionLength'))
  const model = draft.definition
  if (model.capabilities.length === 0 || new Set(model.capabilities).size !== model.capabilities.length)
    issues.push(translate('administration.capability.selectCapability'))
  if (model.capabilities.includes('Fix')) {
    if (!model.patchEntrypoint || model.patchEntrypoint.length > 256
      || /^[A-Za-z]:|^[\\/]/.test(model.patchEntrypoint) || model.patchEntrypoint.split(/[\\/]/).some(part => part === '.' || part === '..'))
      issues.push(translate('challenges.challengeTemplate.validation.patchEntrypointFormat'))
    if (model.patchCommand.length > 64 || model.patchCommand.some(argument => !argument.trim() || argument.length > 4096)
      || model.patchCommand.length > 0 && model.patchCommand.filter(argument => argument === '{entrypoint}').length !== 1)
      issues.push(translate('challenges.challengeTemplate.validation.nonEmptyRequired'))
    if (model.patchTimeoutSeconds !== null && (!Number.isInteger(model.patchTimeoutSeconds) || model.patchTimeoutSeconds < 1 || model.patchTimeoutSeconds > 300))
      issues.push(translate('challenges.challengeTemplate.validation.patchTimeoutRange'))
    if (model.maximumPatchUploadBytes !== null && (!Number.isSafeInteger(model.maximumPatchUploadBytes)
      || model.maximumPatchUploadBytes <= 0 || model.maximumPatchUploadBytes > HARD_MAXIMUM_PATCH_UPLOAD_BYTES))
      issues.push(translate('challenges.challengeTemplate.validation.fixArchiveFormat'))
  }
  if (model.capabilities.includes('Check')) {
    validateChecker(issues, model.checker?.job)
    if (!model.checkRequirements.length) issues.push(translate('administration.capability.checkRequired'))
    if (model.readyTimeoutSeconds !== null && (model.readyTimeoutSeconds <= 0 || !Number.isInteger(model.readyTimeoutSeconds)))
      issues.push(translate('challenges.challengeTemplate.validation.readyTimeoutFormat'))
    if ((model.readyTimeoutSeconds ?? 30) > (model.checker?.job.timeoutSeconds ?? 30))
      issues.push(translate('challenges.challengeTemplate.validation.readinessTimeoutLength'))
    if ((model.patchTimeoutSeconds ?? 60) + (model.checker?.job.timeoutSeconds ?? 30) + 120 >= 2400)
      issues.push(translate('challenges.challengeTemplate.validation.totalFixFormat'))
  }
  if (model.capabilities.includes('Questionnaire')) {
    const questions = model.questionnaire?.questions
    if (!questions?.length || questions.some(question => !question.title?.trim() || !question.body?.trim()
      || !question.options || question.options.length < 2 || question.options.some(option => !option.content?.trim())
      || question.options.filter(option => option.isCorrect).length === 0
      || question.kind !== 'MultipleChoice' && question.options.filter(option => option.isCorrect).length !== 1))
      issues.push(translate('administration.capability.questionnaireInvalid'))
  }
  const runtime = model.runtime
  if (!runtime) {
    if (model.capabilities.some(kind => kind === 'Fix' || kind === 'Check' || kind === 'Control'))
      issues.push(translate('administration.capability.runtimeRequired'))
    return [...new Set(issues)]
  }
  if (runtime.operationTimeoutSeconds !== null && (runtime.operationTimeoutSeconds < 1 || runtime.operationTimeoutSeconds > 300))
    issues.push(translate('challenges.challengeTemplate.validation.runtimeTimeoutRange'))
  const definition = runtime.definition
  const services = definition.kind === 'container' ? definition.services : []
  if (definition.kind === 'container') {
    if (services.length < 1 || services.length > 64 || new Set(services.map(service => service.name)).size !== services.length)
      issues.push(translate('runtime.error.runtimeServicesInvalid'))
    for (const service of services) {
      if (!/^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/.test(service.name)) issues.push(translate('runtime.error.runtimeServicesInvalid'))
      if (!service.image.trim()) issues.push(translate('challenges.validation.containerImageRequired'))
      if (service.image.length > 512) issues.push(translate('challenges.challengeTemplate.validation.containerImageLength'))
      if (service.cpuCores === null || service.cpuCores <= 0 || Math.abs(service.cpuCores * 1000 - Math.round(service.cpuCores * 1000)) > 1e-8
        || service.memoryMiB === null || !Number.isSafeInteger(service.memoryMiB) || service.memoryMiB <= 0)
        issues.push(translate('runtime.error.runtimeServiceResourcesInvalid'))
      if (service.internalPorts.some(port => !validPort(port)) || new Set(service.internalPorts).size !== service.internalPorts.length)
        issues.push(translate('challenges.challengeTemplate.validation.internalPortsRange'))
      validateEnvironment(issues, service.environment, translate('common.label.runtimeEnvironment'))
    }
  }
  else {
    if (!definition.sourceUrl.trim() || !/^[a-f0-9]{64}$/i.test(definition.sha256.trim()))
      issues.push(translate('administration.capability.ovaInvalid'))
    if (runtime.limits.memoryBytes === null || runtime.limits.memoryBytes < 1024 * 1024)
      issues.push(translate('challenges.challengeTemplate.validation.runtimeMemoryFormat'))
  }
  for (const binding of runtime.urlBindings) {
    if (!binding.urlTemplate.trim()) issues.push(translate('challenges.challengeTemplate.validation.accessDisplayRequired'))
    if (definition.kind === 'container' && (!validPort(binding.containerPort)
      || !services.some(service => service.name === binding.serviceName && service.internalPorts.includes(binding.containerPort))))
      issues.push(translate('runtime.error.runtimeServiceReferenceInvalid'))
  }
  if (model.capabilities.includes('Fix') && !services.some(service => service.name === model.patchTargetServiceName))
    issues.push(translate('runtime.error.runtimeServiceReferenceInvalid'))
  if (model.capabilities.includes('Break')) for (const target of model.flagEnvironmentTargets) {
    if (!services.some(service => service.name === target.serviceName)) issues.push(translate('runtime.error.runtimeServiceReferenceInvalid'))
    if (!environmentNamePattern.test(target.variableName ?? '') || target.variableName?.toUpperCase().startsWith('NOCTF_'))
      issues.push(translate('challenges.challengeTemplate.error.flagEnvironmentVariableInvalid'))
  }
  if (model.capabilities.includes('Check') && !services.some(service => service.name === model.checker?.targetServiceName))
    issues.push(translate('runtime.error.runtimeServiceReferenceInvalid'))
  if (model.capabilities.includes('Control') && (!model.control?.urlTemplate?.trim()
    || definition.kind === 'container' && !services.some(service => service.name === model.control?.serviceName)))
    issues.push(translate('runtime.error.runtimeServiceReferenceInvalid'))
  if (model.capabilities.includes('Break')) for (const store of model.flagStores) {
    if (!store.name?.trim() || !services.some(service => service.name === store.targetServiceName))
      issues.push(translate('runtime.error.runtimeServiceReferenceInvalid'))
    if (store.delivery === 'RunnerCommand' && !store.injectionCommand?.trim())
      issues.push(translate('challenges.challengeTemplate.validation.awdFlagFormat'))
  }
  return [...new Set(issues)]
}
