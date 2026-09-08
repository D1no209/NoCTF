

import { LockKeyhole } from '@lucide/vue'
import { authenticationGetMySchoolIdentity, authenticationUpdateMySchoolIdentity } from '../../api'

type Events = { dirty: [value: boolean] }

/** Owns state, effects and commands for SchoolIdentityForm. */
export function useSchoolIdentityForm(emit: { (event: "dirty", ...args: [value: boolean]): void }) {
  const fullName = ref('')

  const studentNumber = ref('')

  const saved = ref({ fullName: '', studentNumber: '' })

  const loading = ref(true)

  const loaded = ref(false)

  const pending = ref(false)

  const retentionDays = ref<number | null>(null)

  const error = ref<string | null>(null)

  const success = ref(false)

  const fieldErrors = ref<Record<string, string[]>>({})

  const fieldError = (name: string) => Object.entries(fieldErrors.value).find(([key]) => key.toLowerCase() === name.toLowerCase())?.[1]?.join(' ')

  const dirty = computed(() => fullName.value !== saved.value.fullName || studentNumber.value !== saved.value.studentNumber)

  watch(dirty, value => { emit('dirty', value); if (value) success.value = false })

  async function load() {
    loading.value = true
    error.value = null
    try {
      const result = await authenticationGetMySchoolIdentity()
      if (result.error || !result.data) throw result.error
      fullName.value = result.data.fullName ?? ''
      studentNumber.value = result.data.studentNumber ?? ''
      retentionDays.value = result.data.ipRetentionDays ?? null
      saved.value = { fullName: fullName.value, studentNumber: studentNumber.value }
      loaded.value = true
    }
    catch (e) { error.value = parseApiError(e).message }
    finally { loading.value = false }
  }

  async function save() {
    if (!loaded.value || pending.value) return
    pending.value = true
    error.value = null
    success.value = false
    fieldErrors.value = {}
    const snapshot = { fullName: fullName.value.trim(), studentNumber: studentNumber.value.trim() }
    try {
      const result = await authenticationUpdateMySchoolIdentity({ body: snapshot })
      if (result.error) throw result.error
      fullName.value = snapshot.fullName
      studentNumber.value = snapshot.studentNumber
      saved.value = snapshot
      success.value = true
    }
    catch (e) {
      const parsed = parseApiError(e)
      error.value = parsed.message
      fieldErrors.value = parsed.fieldErrors ?? {}
    }
    finally { pending.value = false }
  }

  onMounted(load)

  return {
      LockKeyhole,
      fullName,
      studentNumber,
      saved,
      loading,
      loaded,
      pending,
      retentionDays,
      error,
      success,
      fieldError,
      dirty,
      load,
      save
    }
}

export type SchoolIdentityFormViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useSchoolIdentityForm>>>
