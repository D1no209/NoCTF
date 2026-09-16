<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionContainerViewState } from '~/features/admin/useDefinitionContainer'

const viewProps = defineProps<{ state: DefinitionContainerViewState }>()
const { isAwdp, flagEnvDisabled, hasAdvanced, hasMetadata, hasSecurity, definition, disabled, onUpdateModelValueDefinitionCommand, onUpdateModelValueDefinitionContainerPorts, onUpdateModelValueDefinitionInternalPorts, onUpdateModelValueDefinitionEnvironment, onUpdateModelValueDefinitionLabels, onUpdateModelValueDefinitionSecurityCapDrop, onUpdateModelValueDefinitionSecurityCapAdd } = toRefs(viewProps.state)
</script>

<template>
  <FieldGroup>
    <DefinitionSection :title="$t('ui.basics')" :collapsible="false" accent-title>
      <Field>
        <FieldLabel>{{ $t('ui.mirror') }}</FieldLabel>
        <Input
          v-model="definition.image"
          :placeholder="$t('ui.registryExampleComChallengeLatest')"
          class="font-mono text-sm"
          :disabled="disabled"
        />
      </Field>
    </DefinitionSection>

    <DefinitionSection :title="$t('ui.network')" :collapsible="false" accent-title>
      <div class="grid gap-4" :class="isAwdp ? 'sm:grid-cols-2' : ''">
        <Field>
          <FieldLabel>{{ $t('ui.externalPort') }}</FieldLabel>
          <NumberListEditor
            :model-value="definition.containerPorts"
            :placeholder="$t('ui.containerPort')"
            :add-label="$t('ui.addPort')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueDefinitionContainerPorts"
          />
        </Field>
        <Field v-if="isAwdp">
          <FieldLabel>{{ $t('ui.internalPort') }}</FieldLabel>
          <NumberListEditor
            :model-value="definition.internalPorts"
            :placeholder="$t('ui.onlyPortsReachableWithinThePlatform')"
            :add-label="$t('ui.addInternalPort')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueDefinitionInternalPorts"
          />
        </Field>
      </div>
    </DefinitionSection>

    <DefinitionSection :title="$t('ui.advancedSettings')" :default-open="hasAdvanced" accent-title>
      <Field>
        <FieldLabel>{{ $t('ui.startCommand') }}</FieldLabel>
        <StringListEditor
          :model-value="definition.command"
          :placeholder="$t('ui.parametersSuchAsPort')"
          :add-label="$t('ui.addParameters')"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueDefinitionCommand"
        />
      </Field>
      <Field>
        <FieldLabel>{{ $t('ui.flagEnvironmentVariableName') }}</FieldLabel>
        <Input
          v-model="definition.flagEnvironmentVariableName"
          :placeholder="$t('ui.flag')"
          class="font-mono text-sm sm:max-w-xs"
          :disabled="flagEnvDisabled"
        />
      </Field>
      <Field v-if="!isAwdp">
        <FieldLabel>{{ $t('ui.internalPortOptional') }}</FieldLabel>
        <NumberListEditor
          :model-value="definition.internalPorts"
          :placeholder="$t('ui.onlyPortsReachableWithinThePlatform')"
          :add-label="$t('ui.addInternalPort')"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueDefinitionInternalPorts"
        />
      </Field>
    </DefinitionSection>

    <DefinitionSection :title="$t('ui.environmentMetadata')" :default-open="hasMetadata" accent-title>
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

    <DefinitionSection :title="$t('ui.securityOptions')" :default-open="hasSecurity" accent-title>
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
