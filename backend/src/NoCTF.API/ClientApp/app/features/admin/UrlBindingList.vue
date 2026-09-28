<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { UrlBindingModel } from '~/utils/game-config'
import { UrlExposure } from '~/utils/game-config'
import { useUrlBindingList } from './useUrlBindingList'
import View from '~/components/views/admin/UrlBindingListView.vue'

const props = withDefaults(defineProps<{
  modelValue: UrlBindingModel[]
  /** 允许的暴露范围;只传一个值时锁定。 */
  exposureOptions?: { value: number; label: string }[]
  /** Compose 运行时需要选择服务名。 */
  showServiceName?: boolean
  addLabel?: string
  /** 访问入口使用连接格式预设；控制检查入口仍要求 URL。 */
  allowCustomDisplay?: boolean
  disabled?: boolean
}>(), {
  exposureOptions: () => [
    { value: UrlExposure.OwnerOnly, label: "ui.onlyVisibleToTheTeamItself" },
    { value: UrlExposure.Participants, label: "ui.visibleToAllContestants" },
  ],
  showServiceName: false,
  addLabel: translate("ui.addAccessPortal"),
  allowCustomDisplay: true,
  disabled: false,
})
const emit = defineEmits<{ 'update:modelValue': [value: UrlBindingModel[]] }>()
const state = bindViewState(useUrlBindingList(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
