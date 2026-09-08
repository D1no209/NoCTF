<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useSchoolIdentityForm } from './useSchoolIdentityForm'
import View from '~/components/views/account/SchoolIdentityFormView.vue'

const emit = defineEmits<{ dirty: [value: boolean] }>()
const state = bindViewState(useSchoolIdentityForm(emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
