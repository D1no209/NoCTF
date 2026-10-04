import { message as describeMessage } from '../../utils/i18n'
import { ref } from 'vue'
import { toast } from '../../utils/message-toast'

/** Development-only interaction samples. No business data is sent or persisted. */
export function useUiCheck() {
  const { isDark, toggle: toggleTheme } = useTheme()
  const { isEnglish, switchLocale } = useLocale()
  const name = ref('')
  const amount = ref<number | string>(4)
  const when = ref('')
  const selection = ref('')
  const submitted = ref(false)
  const fileCount = ref(0)
  const dialogOpen = ref(false)
  const dialogName = ref('')
  function submit() { submitted.value = true; toast.success(describeMessage('preview.valid')) }
  function showError() { toast.error(describeMessage('preview.error')) }
  function fileChanged(event: Event) { fileCount.value = (event.target as HTMLInputElement).files?.length ?? 0 }
  function submitDialog() { dialogOpen.value = false; toast.success(describeMessage('preview.valid')) }
  return { isDark, toggleTheme, isEnglish, switchLocale, name, amount, when, selection, submitted, fileCount, submit, showError, fileChanged, dialogOpen, dialogName, submitDialog }
}

export type UiCheckViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useUiCheck>>
