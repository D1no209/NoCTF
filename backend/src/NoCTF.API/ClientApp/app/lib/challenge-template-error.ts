import { parseApiError } from '../utils/api-error'
import { currentLocale, translate } from '../utils/i18n'

const definitionDiagnostics: Record<string, string> = {
  'Runtime image is required.': "ui.containerImageIsRequired",
  'Runtime image cannot exceed 512 characters.': "ui.containerImageCannotExceed512Characters",
  'Runtime internal ports must be between 1 and 65535.': "ui.internalPortsMustBeIntegersBetween1And65535",
  'Runtime internal ports cannot contain duplicates.': "ui.internalPortsCannotContainDuplicates",
  'Runtime security must drop all capabilities.': "ui.capDropAll",
  'Runtime TtlSeconds must be between 1 and 604800 when configured.': "ui.runtimeLifetimeMustBeBetween1And604800Seconds",
  'Runtime OperationTimeoutSeconds must be between 1 and 300 when configured.': "ui.runtimeOperationTimeoutMustBeBetween1And300Seconds",
  'Runtime resource limits are required.': "ui.message4",
  'Runtime resource limits must be positive.': "ui.runtimeMemoryCpuAndProcessLimitsMustBePositive",
  'Runtime URL bindings require a valid exposure and template.': "ui.theBindingRequiresAValidExposureAndTemplate",
  'Runtime URL bindings only allow HOST and PORT placeholders.': "ui.theAccessDisplayTemplateCanOnlyUseTheAndPlaceholders",
  'Runtime URL bindings must expand to an absolute URI.': "ui.theControlCheckUrlTemplateMustExpandToAnAbsolute",
  'Runtime URL binding ports must be between 1 and 65535.': "ui.165535",
  'Container URL bindings require ContainerPort.': "ui.message5",
  'Container URL bindings require a dynamic port mapping.': "ui.theAccessUrlPortMustAlsoAppearInThePublished",
  'PerTeam Container runtimes require FlagEnvironmentVariableName.': "ui.perTeamFlagsRequireAFlagEnvironmentVariableName",
  'Flag environment variables cannot use the NOCTF_ prefix.': "ui.theFlagEnvironmentVariableNameCannotUseTheNoctfPrefix",
  'CTF runtimes must use PerTeam allocation.': "ui.ctfRuntimesMustUsePerTeamAllocation",
  'CTF runtimes must use PerTeam flags injected into the runtime environment.': "ui.ctfContainerChallengesMustUsePerTeamFlags",
  'CTF runtime URL bindings must use OwnerOnly exposure.': "ui.ctfAccessUrlsMustBeVisibleOnlyToTheirOwning",
  'AWD runtimes must use PerTeam allocation.': "ui.awdRuntimesMustUsePerTeamAllocation",
  'AWD runtimes must use AwdRotation flags.': "ui.awdRuntimesMustUseRotatingFlags",
  'AWD runtimes only support Container or Compose.': "ui.awdDockerCompose",
  'AWD runtimes require at least one Participants access URL.': "ui.awdRuntimesMustProvideAtLeastOneParticipantVisibleAccess",
  'FlagInjection is required when Runtime is configured.': "ui.anAwdRuntimeRequiresAFlagInjectionCommand",
  'FlagInjection.Command must be a non-empty raw template containing ${FLAG}.': "ui.theAwdFlagInjectionCommandMustContain",
  'FlagInjection.TimeoutSeconds must be between 1 and 300.': "ui.theAwdFlagInjectionTimeoutMustBeBetween1And",
  'FlagInjection.ServiceName is required for Compose runtimes.': "ui.aComposeRuntimeRequiresAFlagInjectionTargetService",
  'Runtime is required when Checker is configured.': "ui.enableTheRuntimeBeforeEnablingTheChecker",
  'Checker.Image is required.': "ui.checker",
  'Checker.TargetServiceName is required only for Compose Runtime.': "ui.aComposeCheckerRequiresATargetServiceName",
  'AWDP player Runtime allocation must be PerTeam.': "ui.awdpRuntimesMustUsePerTeamAllocation",
  'AWDP player Runtime FlagSource must be PerTeam.': "ui.awdpRuntimesMustUsePerTeamFlags",
  'AWDP requires a Docker or Kubernetes Container runtime.': "ui.awdpOnlySupportsSingleContainerRuntimes",
  'AWDP target Runtime must declare exactly one InternalPort.': "ui.awdpRequiresExactlyOneInternalPort",
  'AWDP player Runtime must publish exactly one attack port.': "ui.awdpRequiresExactlyOnePublishedPort",
  'AWDP player Runtime must publish its single checker target port.': "ui.theAwdpInternalAndPublishedPortsMustMatch",
  'AWDP player Runtime must publish an OwnerOnly access URL.': "ui.awdp1",
  'AWDP player Runtime URL bindings must use OwnerOnly exposure.': "ui.awdpAccessUrlsMustBeVisibleOnlyToTheirOwning",
  'AWDP player Runtime URL bindings must target its checker port.': "ui.theAwdpAccessUrlPortMustMatchTheInternalPort",
  'PatchEntrypoint is required.': "ui.message6",
  'PatchEntrypoint cannot exceed 256 characters.': "ui.thePatchEntrypointCannotExceed256Characters",
  'PatchEntrypoint must be a safe relative path.': "ui.thePatchEntrypointMustBeASafeRelativePath",
  'PatchTimeoutSeconds must be between 1 and 300 when configured.': "ui.patchTimeoutMustBeBetween1And300Seconds",
  'PatchTimeoutSeconds must be between 1 and 300.': "ui.patchTimeoutMustBeBetween1And300Seconds",
  'ReadyTimeoutSeconds must be positive when configured.': "ui.readyTimeoutMustBePositive",
  'ReadyTimeoutSeconds must be positive.': "ui.readyTimeoutMustBePositive",
  'ReadyTimeoutSeconds cannot exceed Checker.TimeoutSeconds.': "ui.theReadinessTimeoutCannotExceedTheCheckerTimeout",
  'PatchCommand cannot contain more than 64 arguments.': "ui.thePatchCommandCanContainAtMost64Arguments",
  'PatchCommand cannot contain blank arguments.': "ui.thePatchCommandCannotContainBlankArguments",
  'PatchCommand arguments cannot exceed 4096 characters.': "ui.eachPatchCommandArgumentCanContainAtMost4096Characters",
  'PatchCommand must contain exactly one standalone {entrypoint} argument.': "ui.aNonEmptyPatchCommandMustContainExactlyOneStandalone",
  'AWDP Fix execution budget must remain below the dedicated handler timeout.': "ui.theTotalFixExecutionBudgetMustRemainBelowTheDedicated",
  'Checker.TimeoutSeconds must be between 1 and 1800.': "ui.checkerTimeoutMustBeBetween1And1800Seconds",
}

function splitDiagnostics(message: string): string[] {
  return message
    .split(/(?<=\.)\s+(?=[A-Z])/)
    .map(item => item.trim())
    .filter(Boolean)
}

function localizeDiagnostic(message: string): string {
  const direct = definitionDiagnostics[message]
  if (direct) return translate(direct)
  if (/definition|schemaVersion|JSON/i.test(message))
    return translate("ui.theChallengeDefinitionVersionOrJsonFormatIsInvalid")
  if (currentLocale() === 'en') return message
  return translate("ui.theServerReturnedAnUnrecognizedValidationReason", { reason: message })
}

export function challengeTemplateWriteErrorMessages(error: unknown): string[] {
  const problem = error && typeof error === 'object'
    ? error as { code?: string, detail?: string, title?: string, errors?: Record<string, string[]> }
    : null
  switch (problem?.code) {
    case 'ResourceIdConflict':
      return [translate("ui.theChallengeTemplateResourceIdentifierConflictsWithAnExistingResource")]
    case 'ActiveCompetitionModeConflict':
      return [translate("ui.thisTemplateIsReferencedByAnActiveCompetitionSoIts")]
    case 'ActiveRuntimeDefinitionConflict':
      return [translate("ui.thisTemplateStillHasActiveRuntimesStopThemBeforeChanging")]
    case 'OwnerIncludedInManagerSet':
      return [translate("ui.theTemplateOwnerCannotAlsoAppearInTheCollaboratorList")]
    case 'UserNotFound':
      return [translate("ui.theCollaboratorListContainsAUserThatDoesNotExist")]
    case 'RoleNotEligible':
      return [translate("ui.collaboratorsMustHaveTheOrganizerOrAdministratorRole")]
  }
  const diagnostics = [
    problem?.detail,
    ...Object.values(problem?.errors ?? {}).flat(),
  ]
    .filter((message): message is string => typeof message === 'string' && !!message.trim())
    .flatMap(splitDiagnostics)
    .map(localizeDiagnostic)
  if (diagnostics.length > 0) return [...new Set(diagnostics)]

  return [parseApiError(error).message]
}

export function challengeTemplateWriteErrorMessage(error: unknown): string {
  return challengeTemplateWriteErrorMessages(error).join(translate('；'))
}
