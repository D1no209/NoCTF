import { toRefs } from 'vue'

import { Plus, X } from '@lucide/vue'
import type { UrlBindingModel } from '../../utils/game-config'
import { UrlExposure } from '../../utils/game-config'

/** Owns state, effects and commands for UrlBindingList. */
export function useUrlBindingList(props: Readonly<Omit<{
  modelValue: UrlBindingModel[]
  /** 允许的暴露范围;只传一个值时锁定。 */
  exposureOptions?: { value: number; label: string }[]
  /** Compose 运行时需要选择服务名。 */
  showServiceName?: boolean
  addLabel?: string
  /** 访问入口允许输出命令等自定义显示文本；控制检查入口仍要求 URL。 */
  allowCustomDisplay?: boolean
  disabled?: boolean
}, "exposureOptions" | "showServiceName" | "addLabel" | "allowCustomDisplay" | "disabled"> & Required<Pick<{
  modelValue: UrlBindingModel[]
  /** 允许的暴露范围;只传一个值时锁定。 */
  exposureOptions?: { value: number; label: string }[]
  /** Compose 运行时需要选择服务名。 */
  showServiceName?: boolean
  addLabel?: string
  /** 访问入口允许输出命令等自定义显示文本；控制检查入口仍要求 URL。 */
  allowCustomDisplay?: boolean
  disabled?: boolean
}, "exposureOptions" | "showServiceName" | "addLabel" | "allowCustomDisplay" | "disabled">>>,
emit: { (event: "update:modelValue", ...args: [value: UrlBindingModel[]]): void }) {
  function update(index: number, patch: Partial<UrlBindingModel>): void {
    const next = props.modelValue.map((binding, i) => (i === index ? { ...binding, ...patch } : binding))
    emit('update:modelValue', next)
  }

  function remove(index: number): void {
    emit('update:modelValue', props.modelValue.filter((_, i) => i !== index))
  }

  function add(): void {
    emit('update:modelValue', [
      ...props.modelValue,
      {
        urlTemplate: 'http://{HOST}:{PORT}',
        exposure: props.exposureOptions[0]?.value ?? UrlExposure.Participants,
        containerPort: null,
        serviceName: '',
      },
    ])
  }

  return {
      ...toRefs(props),
      Plus,
      X,
      update,
      remove,
      add
    }
}

export type UrlBindingListViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useUrlBindingList>>>
