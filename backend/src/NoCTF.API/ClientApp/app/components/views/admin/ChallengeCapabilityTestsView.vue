<script setup lang="ts">
import type { ChallengeCapabilityTestsViewState } from '~/features/admin/useChallengeCapabilityTests'
defineProps<{ state: ChallengeCapabilityTestsViewState; definitionDirty: boolean }>()
</script>

<template>
  <div class="flex flex-col gap-5">
    <div class="flex items-center justify-between gap-3">
      <h2 class="text-lg font-semibold">{{ $t('administration.capabilityTests.title') }}</h2>
      <Button variant="outline" :disabled="state.busy" @click="state.refresh">{{ $t('common.label.reload') }}</Button>
    </div>
    <Alert v-if="definitionDirty"><AlertDescription>{{ $t('administration.capabilityTests.unsaved') }}</AlertDescription></Alert>
    <Alert v-if="state.error" variant="destructive"><AlertDescription>{{ $message(state.error) }}</AlertDescription></Alert>
    <Alert v-if="state.timedOut" variant="destructive"><AlertDescription>{{ $t('administration.capabilityTests.pollTimeout') }}</AlertDescription></Alert>
    <section v-if="state.has('Break')" class="flex flex-col gap-3">
      <h3 class="font-semibold">{{ $t('administration.capability.break') }}</h3>
      <UiForm validation="feature" class="flex flex-col gap-3" @submit.prevent="state.testBreak">
        <Field><FieldLabel for="capability-break-flag">{{ $t('administration.label.flagContent') }}</FieldLabel><Input id="capability-break-flag" v-model="state.flag" :disabled="state.disabled" autocomplete="off" required /></Field>
        <Field orientation="horizontal"><Checkbox id="capability-break-runtime" v-model="state.useRuntimeFlag" :disabled="state.disabled || !state.runtimeReady" /><FieldLabel for="capability-break-runtime">{{ $t('administration.capabilityTests.runtimeFlag') }}</FieldLabel></Field>
        <Button class="self-start" type="submit" :disabled="state.disabled || !state.flag || state.useRuntimeFlag && !state.runtimeReady"><Spinner v-if="state.busy" data-icon="inline-start" />{{ $t('administration.capabilityTests.evaluate') }}</Button>
      </UiForm>
      <Badge v-if="state.breakResult?.match" class="self-start">{{ $t(`administration.capabilityTests.result.${state.breakResult.match}`) }}</Badge>
    </section>
    <section v-if="state.has('Fix')" class="flex flex-col gap-3">
      <Separator /><h3 class="font-semibold">{{ $t('administration.capability.fix') }}</h3>
      <Field><FieldLabel for="capability-fix-archive">{{ $t('administration.capabilityTests.archive') }}</FieldLabel><FileUpload id="capability-fix-archive" accept=".tar.gz" :disabled="state.disabled" @change="state.selectFile" /><FieldDescription>{{ $t('administration.capabilityTests.isolated') }}</FieldDescription></Field>
      <div class="flex flex-wrap gap-2"><Button :disabled="state.disabled || !state.archive" @click="state.testFix(false)"><Spinner v-if="state.busy" data-icon="inline-start" />{{ $t('administration.capabilityTests.fix') }}</Button><Button v-if="state.has('Check')" variant="outline" :disabled="state.disabled || !state.archive" @click="state.testFix(true)">{{ $t('administration.capabilityTests.fixAndCheck') }}</Button></div>
    </section>
    <section v-if="state.has('Check') || state.has('Control')" class="flex flex-col gap-3">
      <Separator /><Alert v-if="!state.runtimeReady"><AlertDescription>{{ $t('administration.capabilityTests.runtimeRequired') }}</AlertDescription></Alert>
      <div class="flex flex-wrap gap-2"><Button v-if="state.has('Check')" :disabled="state.disabled || !state.runtimeReady" @click="state.testCheck">{{ $t('administration.capabilityTests.check') }}</Button><Button v-if="state.has('Control')" :disabled="state.disabled || !state.runtimeReady" @click="state.testControl">{{ $t('administration.capabilityTests.control') }}</Button></div>
      <div v-if="state.controlResult?.result" class="flex flex-wrap items-center gap-3"><Badge>{{ $t(`administration.capabilityTests.result.${state.controlResult.result}`) }}</Badge><span v-if="state.controlResult.controllerIdentifier" class="font-mono">{{ state.controlResult.controllerIdentifier }}</span><span v-if="state.controlResult.executionFailure">{{ $t(`administration.capabilityTests.failure.${state.controlResult.executionFailure}`) }}</span></div>
    </section>
    <section v-if="state.has('Questionnaire')" class="flex flex-col gap-4">
      <Separator /><h3 class="font-semibold">{{ $t('administration.capability.questionnaire') }}</h3>
      <div v-for="question in state.questionnaireQuestions" :key="question.id" class="flex flex-col gap-3">
        <h4 class="font-medium">{{ question.title }}</h4><MarkdownContent :source="question.body ?? ''" :assets="state.assets" />
        <Field v-for="option in question.options" :key="option.id" orientation="horizontal">
          <Checkbox :id="`test-option-${option.id}`" :model-value="!!question.id && !!option.id && (state.answers[question.id] ?? []).includes(option.id)" :disabled="state.disabled" @update:model-value="state.selectOption(question.id, option.id, question.kind === 'MultipleChoice', $event)" />
          <FieldLabel :for="`test-option-${option.id}`"><MarkdownContent :source="option.content ?? ''" :assets="state.assets" /></FieldLabel>
        </Field>
        <Badge v-if="question.resultMessage" class="self-start">{{ $message(question.resultMessage) }}</Badge>
      </div>
      <Button class="self-start" :disabled="state.disabled" @click="state.testQuestionnaire">{{ $t('administration.capabilityTests.evaluate') }}</Button>
    </section>
    <section v-if="state.execution" class="flex flex-col gap-3" aria-live="polite">
      <Separator /><h3 class="font-semibold">{{ $t('administration.capabilityTests.execution') }}</h3>
      <Badge v-if="state.execution.state" class="self-start">{{ $t(`administration.capabilityTests.state.${state.execution.state}`) }}</Badge>
      <span v-if="state.execution.failure" class="text-destructive">{{ $t(`administration.capabilityTests.failure.${state.execution.failure}`) }}</span>
      <span v-if="state.execution.fixResult">{{ $t('administration.capability.fix') }} · {{ $t(`administration.capabilityTests.result.${state.execution.fixResult}`) }}</span>
      <Table v-if="state.execution.probes?.length">
        <TableHeader><TableRow><TableHead>{{ $t('administration.capabilityTests.probeTitle') }}</TableHead><TableHead>{{ $t('administration.capabilityTests.resultTitle') }}</TableHead></TableRow></TableHeader>
        <TableBody><template v-for="(probe, index) in state.execution.probes" :key="index"><TableRow v-if="probe.kind && probe.result"><TableCell>{{ $t(`administration.capabilityTests.probe.${probe.kind}`) }}</TableCell><TableCell>{{ $t(`administration.capabilityTests.result.${probe.result}`) }}</TableCell></TableRow></template></TableBody>
      </Table>
    </section>
  </div>
</template>
