import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import type { MutationAction } from '@/features/shared/feedback'

// V1 feedback glue: wraps a feature-module mutation with toast notifications
// so templates can keep calling `mutation.mutate(...)` / `mutation.isPending`.
// A message may be a key or a resolver returning the text (dynamic variants;
// the success resolver also receives the mutation result).
// `hooks` runs theme-side effects (e.g. closing a dialog) around the toast.
export function useToastMutation<TVars = void>(
  mutation: MutationAction<TVars>,
  messages: { success?: string | ((data?: unknown) => string), error?: string | (() => string) },
  hooks?: { onSuccess?: () => void, onError?: () => void },
) {
  const { t } = useI18n()
  return {
    isPending: mutation.isPending,
    mutate: (variables: TVars) => mutation.mutate(variables, {
      onSuccess: (data) => {
        hooks?.onSuccess?.()
        const key = typeof messages.success === 'function' ? messages.success(data) : messages.success
        if (key)
          toast.success(t(key))
      },
      onError: () => {
        hooks?.onError?.()
        const key = typeof messages.error === 'function' ? messages.error() : messages.error
        if (key)
          toast.error(t(key))
      },
    }),
  }
}
