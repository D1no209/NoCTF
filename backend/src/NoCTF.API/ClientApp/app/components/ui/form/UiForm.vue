<script setup lang="ts">
import { nextTick, ref, useId, watch } from 'vue'
import { constraintFeedback } from './validation'
import { currentLocale, translate } from '~/utils/i18n'

defineOptions({ inheritAttrs: false })
const props = withDefaults(defineProps<{ validation?: 'constraints' | 'feature' }>(), { validation: 'constraints' })
const emit = defineEmits<{ submit: [event: SubmitEvent] }>()
const form = ref<HTMLFormElement | null>(null)
const issues = ref<Array<{ id: string; messageId: string; element: HTMLElement; feedback: NonNullable<ReturnType<typeof constraintFeedback>> }>>([])
const formId = useId()
const previousAria = new WeakMap<HTMLElement, { invalid: string | null; message: string | null }>()
let attempted = false
const attempt = ref(0)

function validate() {
  if (!form.value) return true
  const next: typeof issues.value = []
  for (const element of Array.from(form.value.elements)) {
    if (!(element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement || element instanceof HTMLSelectElement)) continue
    const feedback = element.willValidate ? constraintFeedback({
      label: element.labels?.[0]?.textContent?.trim() || element.getAttribute('aria-label') || translate('form.field'),
      validity: element.validity,
      min: element.getAttribute('min') ?? undefined,
      max: element.getAttribute('max') ?? undefined,
      minLength: 'minLength' in element ? element.minLength : undefined,
      maxLength: 'maxLength' in element ? element.maxLength : undefined,
    }) : null
    if (feedback) {
      if (!previousAria.has(element)) previousAria.set(element, { invalid: element.getAttribute('aria-invalid'), message: element.getAttribute('aria-errormessage') })
      const messageId = `${formId}-error-${next.length}`
      element.setAttribute('aria-invalid', 'true')
      element.setAttribute('aria-errormessage', messageId)
      element.dataset.formInvalid = 'true'
      const focusTarget = element.closest('[data-slot="file-upload"]')?.querySelector<HTMLElement>('button') ?? element
      next.push({ id: element.id || String(next.length), messageId, element: focusTarget, feedback })
    } else if (element.dataset.formInvalid) {
      const previous = previousAria.get(element)
      for (const [attribute, value] of [['aria-invalid', previous?.invalid], ['aria-errormessage', previous?.message]]) {
        if (value) element.setAttribute(attribute!, value)
        else element.removeAttribute(attribute!)
      }
      previousAria.delete(element)
      delete element.dataset.formInvalid
    }
  }
  issues.value = next
  return next.length === 0
}

function submit(event: SubmitEvent) {
  event.preventDefault()
  attempted = true
  attempt.value += 1
  if (props.validation === 'feature' || validate()) emit('submit', event)
  else issues.value[0]?.element.focus()
}
function revalidate() { if (attempted && props.validation !== 'feature') validate() }
watch(currentLocale, () => nextTick(revalidate))
</script>

<template>
  <form ref="form" data-slot="form" v-bind="$attrs" novalidate @submit="submit" @input="revalidate" @change="revalidate">
    <span v-for="issue in issues" :id="issue.messageId" :key="issue.messageId" class="sr-only">{{ $t(issue.feedback.key, issue.feedback.values) }}</span>
    <Alert v-if="issues.length" :key="attempt" variant="destructive" role="alert">
      <AlertTitle>{{ $t('form.review') }}</AlertTitle>
      <AlertDescription>
        <ul class="flex flex-col gap-1">
          <li v-for="issue in issues" :key="issue.id">
            <ActionButton class="text-left underline underline-offset-4" @click="issue.element.focus()">{{ $t(issue.feedback.key, issue.feedback.values) }}</ActionButton>
          </li>
        </ul>
      </AlertDescription>
    </Alert>
    <slot />
  </form>
</template>
