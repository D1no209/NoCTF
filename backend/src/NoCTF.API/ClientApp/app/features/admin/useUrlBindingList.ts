import { toRefs } from 'vue'

import { Plus, X } from '@lucide/vue'
import type { UrlBindingModel } from '../../utils/game-config'
import { UrlExposure } from '../../utils/game-config'

const HTTP_DISPLAY_TEMPLATE = 'http://{HOST}:{PORT}'
const NETCAT_DISPLAY_TEMPLATE = 'nc {HOST} {PORT}'

/** Owns state, effects and commands for UrlBindingList. */
export function useUrlBindingList(props: Readonly<Omit<{
  modelValue: UrlBindingModel[]
  /** 允许的暴露范围;只传一个值时锁定。 */
  exposureOptions?: { value: number; label: string }[]
  /** 容器运行时的服务选择。 */
  showServiceName?: boolean
  serviceNames?: string[]
  addLabel?: string
  /** 访问入口使用连接格式预设；控制检查入口仍要求 URL。 */
  allowCustomDisplay?: boolean
  disabled?: boolean
}, "exposureOptions" | "showServiceName" | "serviceNames" | "addLabel" | "allowCustomDisplay" | "disabled"> & Required<Pick<{
  modelValue: UrlBindingModel[]
  /** 允许的暴露范围;只传一个值时锁定。 */
  exposureOptions?: { value: number; label: string }[]
  /** 容器运行时的服务选择。 */
  showServiceName?: boolean
  serviceNames?: string[]
  addLabel?: string
  /** 访问入口使用连接格式预设；控制检查入口仍要求 URL。 */
  allowCustomDisplay?: boolean
  disabled?: boolean
}, "exposureOptions" | "showServiceName" | "serviceNames" | "addLabel" | "allowCustomDisplay" | "disabled">>>,
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
        urlTemplate: HTTP_DISPLAY_TEMPLATE,
        exposure: props.exposureOptions[0]?.value ?? UrlExposure.Participants,
        containerPort: null,
        serviceName: props.serviceNames[0] ?? '',
      },
    ])
  }

  return {
      ...toRefs(props),
      Plus,
      X,
      displayTemplateOptions: [HTTP_DISPLAY_TEMPLATE, NETCAT_DISPLAY_TEMPLATE],
      update,
      remove,
      add
    }
}

export type UrlBindingListViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useUrlBindingList>>>
