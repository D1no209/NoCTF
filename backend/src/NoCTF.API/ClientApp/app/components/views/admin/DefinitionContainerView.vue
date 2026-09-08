<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionContainerViewState } from '~/features/admin/useDefinitionContainer'

const viewProps = defineProps<{ state: DefinitionContainerViewState }>()
const { FlagSource, isAwdp, flagEnvDisabled, hasMetadata, hasSecurity, definition, flagSource, disabled, onUpdateModelValueDefinitionCommand, onUpdateModelValueDefinitionContainerPorts, onUpdateModelValueDefinitionInternalPorts, onUpdateModelValueDefinitionEnvironment, onUpdateModelValueDefinitionLabels, onUpdateModelValueDefinitionSecurityCapDrop, onUpdateModelValueDefinitionSecurityCapAdd } = toRefs(viewProps.state)
</script>

<template>
  <FieldGroup>
    <DefinitionSection :title="$t('ui.basics')" :collapsible="false">
      <Field>
        <FieldLabel>{{ $t('ui.mirror') }}</FieldLabel>
        <Input
          v-model="definition.image"
          :placeholder="$t('ui.registryExampleComChallengeLatest')"
          class="font-mono text-sm"
          :disabled="disabled"
        />
        <FieldDescription>{{ $t('ui.containerImageAddressUpTo512Characters') }}</FieldDescription>
      </Field>
      <Field>
        <FieldLabel>{{ $t('ui.startCommand') }}</FieldLabel>
        <StringListEditor
          :model-value="definition.command"
          :placeholder="$t('ui.parametersSuchAsPort')"
          :add-label="$t('ui.addParameters')"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueDefinitionCommand"
        />
        <FieldDescription>{{ $t('ui.leaveItBlankToUseTheDefaultEntryOfThe') }}</FieldDescription>
      </Field>
      <Field>
        <FieldLabel>{{ $t('ui.flagEnvironmentVariableName') }}</FieldLabel>
        <Input
          v-model="definition.flagEnvironmentVariableName"
          :placeholder="$t('ui.flag')"
          class="font-mono text-sm sm:max-w-xs"
          :disabled="flagEnvDisabled"
        />
        <FieldDescription v-if="flagSource === FlagSource.Static"> {{ $t('ui.itCanOnlyBeConfiguredWhenIndependentFlagForEach') }} </FieldDescription>
        <FieldDescription v-else>{{ $t('ui.theEnvironmentVariableNameUsedWhenInjectingFlagByTeam') }}</FieldDescription>
      </Field>
    </DefinitionSection>

    <DefinitionSection :title="$t('ui.network')" :collapsible="false">
      <div class="grid gap-4 sm:grid-cols-2">
        <Field>
          <FieldLabel>{{ $t('ui.externalPort') }}</FieldLabel>
          <NumberListEditor
            :model-value="definition.containerPorts"
            :placeholder="$t('ui.containerPort')"
            :add-label="$t('ui.addPort')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueDefinitionContainerPorts"
          />
          <FieldDescription>{{ $t('ui.theHostPortIsRandomlyAssignedByDockerAndPlayers') }}</FieldDescription>
        </Field>
        <Field>
          <FieldLabel>{{ isAwdp ? $t('ui.internalPort') : $t('ui.internalPortOptional') }}</FieldLabel>
          <NumberListEditor
            :model-value="definition.internalPorts"
            :placeholder="$t('ui.onlyPortsReachableWithinThePlatform')"
            :add-label="$t('ui.addInternalPort')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueDefinitionInternalPorts"
          />
          <FieldDescription v-if="isAwdp">{{ $t('ui.awdpRequiresExactly1InternalPortThroughWhichTheChecker') }}</FieldDescription>
          <FieldDescription v-else>{{ $t('ui.aPortThatIsNotExposedToPlayersAndIs') }}</FieldDescription>
        </Field>
      </div>
    </DefinitionSection>

    <DefinitionSection
      :title="$t('ui.environmentMetadata')"
      :hint="$t('ui.environmentVariablesAndLabelsMostChallengesDoNotNeedThese')"
      :default-open="hasMetadata"
    >
      <Field>
        <FieldLabel>{{ $t('ui.environmentVariables') }}</FieldLabel>
        <KeyValueEditor
          :model-value="definition.environment"
          :key-placeholder="$t('ui.variableName')"
          :value-placeholder="$t('ui.value')"
          :add-label="$t('ui.addEnvironmentVariables')"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueDefinitionEnvironment"
        />
      </Field>
      <Field>
        <FieldLabel>{{ $t('ui.label') }}</FieldLabel>
        <KeyValueEditor
          :model-value="definition.labels"
          :key-placeholder="$t('ui.tagName')"
          :value-placeholder="$t('ui.value')"
          :add-label="$t('ui.addTag')"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueDefinitionLabels"
        />
      </Field>
    </DefinitionSection>

    <DefinitionSection
      :title="$t('ui.securityOptions')"
      :hint="$t('ui.privilegeEscalationIsDisabledAndAllLinuxCapabilitiesAreDropped')"
      :default-open="hasSecurity"
    >
      <div class="grid gap-3 sm:grid-cols-3">
        <Field orientation="horizontal">
          <Switch id="sec-no-new-privileges" v-model="definition.security.noNewPrivileges" :disabled="disabled" />
          <FieldLabel for="sec-no-new-privileges" class="font-normal">{{ $t('ui.disablePrivilegeEscalationNoNewPrivileges') }}</FieldLabel>
        </Field>
        <Field orientation="horizontal">
          <Switch id="sec-readonly-rootfs" v-model="definition.security.readonlyRootfs" :disabled="disabled" />
          <FieldLabel for="sec-readonly-rootfs" class="font-normal">{{ $t('ui.readOnlyRootFileSystem') }}</FieldLabel>
        </Field>
        <Field orientation="horizontal">
          <Switch id="sec-run-as-non-root" v-model="definition.security.runAsNonRoot" :disabled="disabled" />
          <FieldLabel for="sec-run-as-non-root" class="font-normal">{{ $t('ui.runAsNonRootUser') }}</FieldLabel>
        </Field>
      </div>
      <div class="grid gap-4 sm:grid-cols-2">
        <Field>
          <FieldLabel>{{ $t('ui.capabilityToRemoveCapDrop') }}</FieldLabel>
          <StringListEditor
            :model-value="definition.security.capDrop"
            :placeholder="$t('ui.suchAsNetRaw')"
            :add-label="$t('ui.addRemoveItems')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueDefinitionSecurityCapDrop"
          />
          <FieldDescription>{{ $t('ui.allIsIncludedByDefaultRestoreOnlyTheCapabilitiesThe') }}</FieldDescription>
        </Field>
        <Field>
          <FieldLabel>{{ $t('ui.addedCapabilitiesCapAdd') }}</FieldLabel>
          <StringListEditor
            :model-value="definition.security.capAdd"
            :placeholder="$t('ui.suchAsSysPtrace')"
            :add-label="$t('ui.addCapabilities')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueDefinitionSecurityCapAdd"
          />
        </Field>
      </div>
    </DefinitionSection>
  </FieldGroup>
</template>
