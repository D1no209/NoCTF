<script setup lang="ts">
import { toRefs } from 'vue'
import type { QuestionnaireCapabilityEditorViewState } from '~/features/admin/useQuestionnaireCapabilityEditor'
const props = defineProps<{ state: QuestionnaireCapabilityEditorViewState }>()
const { disabled, questions, kinds, difficulties, addQuestion, removeQuestion, setKind, addOption, removeOption, setCorrect, setExplanation,
  setDifficulty, classifications, setClassifications, canUpload, uploadImage, uploading, error, assets } = toRefs(props.state)
</script>
<template>
  <FieldSet>
    <FieldLegend>{{ $t('administration.capability.questionnaire') }}</FieldLegend>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <FieldGroup v-for="question in questions" :key="question.id">
      <Separator />
      <Field><FieldLabel>{{ $t('administration.capability.questionTitle') }}</FieldLabel><Input v-model="question.title" :disabled="disabled" /></Field>
      <Field><FieldLabel>{{ $t('administration.capability.questionKind') }}</FieldLabel><Select :model-value="question.kind" :disabled="disabled" @update:model-value="setKind(question, $event)">
        <SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem v-for="kind in kinds" :key="kind.value" :value="kind.value">{{ $t(kind.label) }}</SelectItem></SelectContent>
      </Select></Field>
      <Field><FieldLabel>{{ $t('theory.difficulty') }}</FieldLabel><Select :model-value="question.difficulty" :disabled="disabled" @update:model-value="setDifficulty(question, $event)">
        <SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem v-for="difficulty in difficulties" :key="difficulty.value" :value="difficulty.value">{{ $t(difficulty.label) }}</SelectItem></SelectContent>
      </Select></Field>
      <Field><FieldLabel>{{ $t('theory.knowledge') }}</FieldLabel><Input :model-value="classifications(question, 'Knowledge')" :disabled="disabled" @update:model-value="setClassifications(question, 'Knowledge', $event)" /></Field>
      <Field><FieldLabel>{{ $t('theory.tags') }}</FieldLabel><Input :model-value="classifications(question, 'Tag')" :disabled="disabled" @update:model-value="setClassifications(question, 'Tag', $event)" /></Field>
      <Field><FieldLabel>{{ $t('administration.capability.questionBody') }}</FieldLabel><Textarea v-model="question.body" :disabled="disabled" /></Field>
      <MarkdownContent :source="question.body ?? ''" :assets="assets" />
      <Field><FieldLabel>{{ $t('theory.images') }}</FieldLabel><FileUpload accept="image/png,image/jpeg,image/webp" :disabled="disabled || !canUpload(question) || uploading !== null" :pending="uploading === question.id" @change="uploadImage(question, $event)" /><FieldDescription>{{ $t('theory.imageHelp') }}</FieldDescription></Field>
      <Field v-for="option in question.options" :key="option.id" orientation="horizontal">
        <Checkbox :id="`answer-${option.id}`" :model-value="option.isCorrect === true" :disabled="disabled" @update:model-value="setCorrect(question, option.id, $event === true)" />
        <FieldLabel :for="`answer-${option.id}`" class="sr-only">{{ $t('administration.capability.correctAnswer') }}</FieldLabel>
        <Input v-model="option.content" :aria-label="$t('administration.capability.optionContent')" :disabled="disabled" />
        <MarkdownContent :source="option.content ?? ''" :assets="assets" />
        <Button v-if="question.kind !== 'TrueFalse'" type="button" variant="ghost" :disabled="disabled" @click="removeOption(question, option.id)">{{ $t('administration.capability.removeOption') }}</Button>
      </Field>
      <Button v-if="question.kind !== 'TrueFalse'" type="button" variant="outline" :disabled="disabled" @click="addOption(question)">{{ $t('administration.capability.addOption') }}</Button>
      <Field><FieldLabel>{{ $t('administration.capability.explanation') }}</FieldLabel><Textarea :model-value="question.explanation ?? ''" :disabled="disabled" @update:model-value="setExplanation(question, $event)" /></Field>
      <MarkdownContent :source="question.explanation ?? ''" :assets="assets" />
      <Button type="button" variant="ghost" :disabled="disabled" @click="removeQuestion(question.id)">{{ $t('administration.capability.removeQuestion') }}</Button>
    </FieldGroup>
    <Button type="button" variant="outline" :disabled="disabled" @click="addQuestion">{{ $t('administration.capability.addQuestion') }}</Button>
  </FieldSet>
</template>
