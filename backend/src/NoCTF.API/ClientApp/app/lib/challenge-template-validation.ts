import type { NoCtfapiEndpointsCompetitionsGameModeProtocol } from '../api'
import type { DefinitionModel, RunnerJobModel } from '../utils/game-config'
import {
  CtfInteraction,
  FlagSource,
  HARD_MAXIMUM_PATCH_UPLOAD_BYTES,
  parseDefinition,
  RuntimeAllocation,
  UrlExposure,
} from '../utils/game-config'
import { translate } from '../utils/i18n'

const environmentNamePattern = /^[A-Za-z_][A-Za-z0-9_]*$/
const patchEntrypointPlaceholder = '{entrypoint}'
const maximumPatchTimeoutSeconds = 300
const maximumPatchCommandArguments = 64
const maximumPatchCommandArgumentLength = 4096
const patchVerificationExecutionOverheadSeconds = 120
const patchVerificationHandlerTimeoutSeconds = 2400

function addIssue(issues: string[], issue: string): void {
  if (!issues.includes(issue)) issues.push(issue)
}

function validPort(value: number | null): value is number {
  return value !== null && Number.isInteger(value) && value >= 1 && value <= 65535
}

function validateEnvironment(
  issues: string[],
  environment: Record<string, string>,
  label: string,
): void {
  for (const name of Object.keys(environment)) {
    if (!environmentNamePattern.test(name)) {
      addIssue(issues, translate("ui.environmentVariableNameInIsInvalid", { label, name }))
    }
    else if (name.toUpperCase().startsWith('NOCTF_')) {
      addIssue(issues, translate("ui.environmentVariablesInCannotUseTheNoctfPrefix", { label }))
    }
  }
}

function validateRunnerJob(issues: string[], job: RunnerJobModel | null, label: string): void {
  if (!job) return
  if (!job.image.trim()) addIssue(issues, translate("ui.imageIsRequired", { label }))
  else if (job.image.length > 512) addIssue(issues, translate("ui.imageCannotExceed512Characters", { label }))
  if (job.command.some(argument => !argument.trim()))
    addIssue(issues, translate("ui.commandCannotContainBlankArguments", { label }))
  if (job.timeoutSeconds !== null && (job.timeoutSeconds < 1 || job.timeoutSeconds > 1800))
    addIssue(issues, translate("ui.timeoutMustBeBetween1And1800Seconds", { label }))
  validateEnvironment(issues, job.environment, label)
}

function validateAccessDisplayTemplate(issues: string[], template: string): void {
  if (!template.trim()) {
    addIssue(issues, translate("ui.theAccessDisplayTemplateIsRequired"))
    return
  }
  const remaining = template
    .replaceAll('{HOST}', '')
    .replaceAll('{PORT}', '')
  if (remaining.includes('{') || remaining.includes('}')) {
    addIssue(issues, translate("ui.theAccessDisplayTemplateCanOnlyUseTheAndPlaceholders"))
  }
}

function validatePatchSettings(issues: string[], model: DefinitionModel): void {
  if (model.patchEntrypoint) {
    const segments = model.patchEntrypoint.split(/[\\/]/)
    if (model.patchEntrypoint.length > 256)
      addIssue(issues, translate("ui.thePatchEntrypointCannotExceed256Characters"))
    else if (/^[A-Za-z]:[\\/]|^[\\/]/.test(model.patchEntrypoint)
      || segments.some(segment => segment === '.' || segment === '..'))
      addIssue(issues, translate("ui.thePatchEntrypointMustBeASafeRelativePath"))
  }
  if (model.patchCommand.length > 0) {
    if (model.patchCommand.length > maximumPatchCommandArguments)
      addIssue(issues, translate("ui.thePatchCommandCanContainAtMost64Arguments"))
    if (model.patchCommand.some(argument => !argument.trim()))
      addIssue(issues, translate("ui.thePatchCommandCannotContainBlankArguments"))
    if (model.patchCommand.some(argument => argument.length > maximumPatchCommandArgumentLength))
      addIssue(issues, translate("ui.eachPatchCommandArgumentCanContainAtMost4096Characters"))
    if (model.patchCommand.filter(argument => argument === patchEntrypointPlaceholder).length !== 1)
      addIssue(issues, translate("ui.aNonEmptyPatchCommandMustContainExactlyOneStandalone"))
  }
  if (model.patchTimeoutSeconds !== null
    && (model.patchTimeoutSeconds < 1
      || model.patchTimeoutSeconds > maximumPatchTimeoutSeconds)) {
    addIssue(issues, translate("ui.patchTimeoutMustBeBetween1And300Seconds"))
  }
  if (model.readyTimeoutSeconds !== null && model.readyTimeoutSeconds <= 0)
    addIssue(issues, translate("ui.readyTimeoutMustBePositive"))
  if (model.checkerJob) {
    const checkerTimeout = model.checkerJob.timeoutSeconds ?? 60
    const readyTimeout = model.readyTimeoutSeconds ?? 30
    const patchTimeout = model.patchTimeoutSeconds ?? 60
    if (readyTimeout > checkerTimeout)
      addIssue(issues, translate("ui.theReadinessTimeoutCannotExceedTheCheckerTimeout"))
    if (patchTimeout > 0 && checkerTimeout > 0
      && patchTimeout + checkerTimeout + patchVerificationExecutionOverheadSeconds
      >= patchVerificationHandlerTimeoutSeconds) {
      addIssue(issues, translate("ui.theTotalFixExecutionBudgetMustRemainBelowTheDedicated"))
    }
  }
  if (model.maximumPatchUploadBytes !== null
    && (model.maximumPatchUploadBytes <= 0
      || model.maximumPatchUploadBytes > HARD_MAXIMUM_PATCH_UPLOAD_BYTES)) {
    addIssue(issues, translate("ui.theFixArchiveUploadLimitMustBePositiveAndNo"))
  }
}

export interface ChallengeTemplateDraft {
  mode: NoCtfapiEndpointsCompetitionsGameModeProtocol
  title: string
  direction: string
  definitionJson: string
}

/** Mirrors the save-time invariants that can be checked without server state. */
export function validateChallengeTemplateDraft(draft: ChallengeTemplateDraft): string[] {
  const issues: string[] = []
  const title = draft.title.trim()
  const direction = draft.direction.trim()
  if (!title) addIssue(issues, translate("ui.titleIsRequired"))
  else if (title.length > 160) addIssue(issues, translate("ui.titleCannotExceed160Characters"))
  if (!direction) addIssue(issues, translate("ui.directionIsRequired"))
  else if (direction.length > 96) addIssue(issues, translate("ui.directionCannotExceed96Characters"))

  const model = parseDefinition(draft.definitionJson, draft.mode)
  if (!model) {
    addIssue(issues, translate("ui.theChallengeDefinitionCannotBeParsedResetOrCorrectIt"))
    return issues
  }
  if (draft.mode === 'Awdp' && model.checkerFixInput && !model.checkerJob)
    addIssue(issues, translate("ui.enableTheCheckerBeforeProvidingItWithTheFixPackage"))
  const usesPatchChecker = draft.mode === 'Awdp'
    || draft.mode === 'Ctf'
      && model.interactionKind === CtfInteraction.PatchVerification
  if (model.checkerAllowRoot
    && ((draft.mode === 'Awd' && !model.checker) || (usesPatchChecker && !model.checkerJob)))
    addIssue(issues, translate("ui.enableTheCheckerBeforeAllowingItToRunAsRoot"))
  const runtime = model.runtime
  if (!runtime) {
    validateRunnerJob(
      issues,
      draft.mode === 'Awd' ? model.checker?.job ?? null : model.checkerJob,
      translate('Checker'),
    )
    if (draft.mode === 'Awd' && model.checker)
      addIssue(issues, translate("ui.enableTheRuntimeBeforeEnablingTheChecker"))
    return issues
  }

  if (runtime.limits.memoryBytes === null || runtime.limits.memoryBytes <= 0
    || runtime.limits.nanoCpus === null || runtime.limits.nanoCpus <= 0
    || runtime.limits.pidsLimit === null || runtime.limits.pidsLimit <= 0) {
    addIssue(issues, translate("ui.runtimeMemoryCpuAndProcessLimitsMustBePositive"))
  }
  if (runtime.ttlSeconds !== null && (runtime.ttlSeconds < 1 || runtime.ttlSeconds > 604800))
    addIssue(issues, translate("ui.runtimeLifetimeMustBeBetween1And604800Seconds"))
  if (runtime.operationTimeoutSeconds !== null
    && (runtime.operationTimeoutSeconds < 1 || runtime.operationTimeoutSeconds > 300)) {
    addIssue(issues, translate("ui.runtimeOperationTimeoutMustBeBetween1And300Seconds"))
  }

  const definition = runtime.definition
  let publicPorts: number[] = []
  let internalPorts: number[] = []
  if (definition.kind === 'container') {
    if (!definition.image.trim()) addIssue(issues, translate("ui.containerImageIsRequired"))
    else if (definition.image.length > 512) addIssue(issues, translate("ui.containerImageCannotExceed512Characters"))
    publicPorts = [...new Set(definition.containerPorts.filter(validPort))]
    internalPorts = definition.internalPorts.filter(validPort)
    if (definition.containerPorts.some(port => port !== null && !validPort(port)))
      addIssue(issues, translate("ui.publishedPortsMustBeIntegersBetween1And65535"))
    if (definition.internalPorts.some(port => port !== null && !validPort(port)))
      addIssue(issues, translate("ui.internalPortsMustBeIntegersBetween1And65535"))
    if (new Set(internalPorts).size !== internalPorts.length)
      addIssue(issues, translate("ui.internalPortsCannotContainDuplicates"))
    validateEnvironment(issues, definition.environment, translate("ui.runtimeEnvironment"))
    if (runtime.flagSource === FlagSource.PerTeam) {
      const flagVariable = definition.flagEnvironmentVariableName.trim()
      if (!flagVariable) addIssue(issues, translate("ui.perTeamFlagsRequireAFlagEnvironmentVariableName"))
      else if (!environmentNamePattern.test(flagVariable))
        addIssue(issues, translate("ui.theFlagEnvironmentVariableNameIsInvalid"))
      else if (flagVariable.toUpperCase().startsWith('NOCTF_'))
        addIssue(issues, translate("ui.theFlagEnvironmentVariableNameCannotUseTheNoctfPrefix"))
    }
  }
  else {
    if (!definition.composeYaml.trim()) addIssue(issues, translate("ui.dockerComposeContentIsRequired"))
    validateEnvironment(issues, definition.environment, translate("ui.runtimeEnvironment"))
  }

  for (const binding of runtime.urlBindings) {
    validateAccessDisplayTemplate(issues, binding.urlTemplate)
    if (!validPort(binding.containerPort))
      addIssue(issues, translate("ui.everyAccessUrlMustSpecifyAValidContainerPort"))
    if (definition.kind === 'container' && validPort(binding.containerPort)
      && !publicPorts.includes(binding.containerPort)) {
      addIssue(issues, translate("ui.theAccessUrlPortMustAlsoAppearInThePublished"))
    }
    if (definition.kind === 'compose' && !binding.serviceName.trim())
      addIssue(issues, translate("ui.composeAccessUrlsRequireAServiceName"))
  }

  switch (draft.mode) {
    case 'Ctf':
      if (runtime.allocation !== RuntimeAllocation.PerTeam)
        addIssue(issues, translate("ui.ctfRuntimesMustUsePerTeamAllocation"))
      if (model.interactionKind === CtfInteraction.PatchVerification) {
        if (runtime.flagSource !== FlagSource.Static)
          addIssue(issues, translate("ui.ctfPatchVerificationRuntimesCannotInjectFlags"))
        if (definition.kind !== 'container')
          addIssue(issues, translate("ui.ctfPatchVerificationOnlySupportsSingleContainerRuntimes"))
        if (internalPorts.length !== 1)
          addIssue(issues, translate("ui.ctfPatchVerificationRequiresExactlyOneInternalPort"))
        validateRunnerJob(issues, model.checkerJob, translate('Checker'))
        validatePatchSettings(issues, model)
      }
      else if (runtime.flagSource !== FlagSource.Static
        && runtime.flagSource !== FlagSource.PerTeam) {
        addIssue(issues, translate("ui.ctfContainerChallengesMustUsePerTeamFlags"))
      }
      if (runtime.urlBindings.some(binding => binding.exposure !== UrlExposure.OwnerOnly))
        addIssue(issues, translate("ui.ctfAccessUrlsMustBeVisibleOnlyToTheirOwning"))
      break
    case 'Awd':
      if (runtime.allocation !== RuntimeAllocation.PerTeam)
        addIssue(issues, translate("ui.awdRuntimesMustUsePerTeamAllocation"))
      if (runtime.flagSource !== FlagSource.AwdRotation)
        addIssue(issues, translate("ui.awdRuntimesMustUseRotatingFlags"))
      if (!runtime.urlBindings.some(binding => binding.exposure === UrlExposure.Participants))
        addIssue(issues, translate("ui.awdRuntimesMustProvideAtLeastOneParticipantVisibleAccess"))
      if (!model.flagInjection) {
        addIssue(issues, translate("ui.anAwdRuntimeRequiresAFlagInjectionCommand"))
      }
      else {
        if (!model.flagInjection.command.trim() || !model.flagInjection.command.includes('${FLAG}'))
          addIssue(issues, translate("ui.theAwdFlagInjectionCommandMustContain"))
        if (model.flagInjection.timeoutSeconds === null
          || model.flagInjection.timeoutSeconds < 1
          || model.flagInjection.timeoutSeconds > 300)
          addIssue(issues, translate("ui.theAwdFlagInjectionTimeoutMustBeBetween1And"))
        if (definition.kind === 'compose' && !model.flagInjection.serviceName.trim())
          addIssue(issues, translate("ui.aComposeRuntimeRequiresAFlagInjectionTargetService"))
      }
      validateRunnerJob(issues, model.checker?.job ?? null, translate('Checker'))
      if (model.checker && definition.kind === 'compose' && !model.checker.targetServiceName.trim())
        addIssue(issues, translate("ui.aComposeCheckerRequiresATargetServiceName"))
      break
    case 'Awdp':
      if (runtime.allocation !== RuntimeAllocation.PerTeam)
        addIssue(issues, translate("ui.awdpRuntimesMustUsePerTeamAllocation"))
      if (runtime.flagSource !== FlagSource.PerTeam)
        addIssue(issues, translate("ui.awdpRuntimesMustUsePerTeamFlags"))
      if (definition.kind !== 'container')
        addIssue(issues, translate("ui.awdpOnlySupportsSingleContainerRuntimes"))
      if (internalPorts.length !== 1)
        addIssue(issues, translate("ui.awdpRequiresExactlyOneInternalPort"))
      if (publicPorts.length !== 1)
        addIssue(issues, translate("ui.awdpRequiresExactlyOnePublishedPort"))
      if (internalPorts.length === 1 && publicPorts.length === 1 && internalPorts[0] !== publicPorts[0])
        addIssue(issues, translate("ui.theAwdpInternalAndPublishedPortsMustMatch"))
      if (runtime.urlBindings.length === 0)
        addIssue(issues, translate("ui.awdpRequiresAtLeastOneAccessUrl"))
      if (runtime.urlBindings.some(binding => binding.exposure !== UrlExposure.OwnerOnly))
        addIssue(issues, translate("ui.awdpAccessUrlsMustBeVisibleOnlyToTheirOwning"))
      if (internalPorts.length === 1
        && runtime.urlBindings.some(binding => binding.containerPort !== internalPorts[0]))
        addIssue(issues, translate("ui.theAwdpAccessUrlPortMustMatchTheInternalPort"))
      validateRunnerJob(issues, model.checkerJob, translate('Checker'))
      validatePatchSettings(issues, model)
      break
    case 'Koh':
      if (runtime.allocation !== RuntimeAllocation.Shared)
        addIssue(issues, translate("ui.kohRuntimesMustUseSharedAllocation"))
      break
  }
  return issues
}
