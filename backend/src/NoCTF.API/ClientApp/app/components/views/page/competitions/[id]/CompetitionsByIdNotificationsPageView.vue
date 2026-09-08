<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdNotificationsPageViewState } from '~/features/routes/competitions/[id]/useCompetitionsByIdNotificationsPage'

const viewProps = defineProps<{ state: CompetitionsByIdNotificationsPageViewState }>()
const {  } = toRefs(viewProps.state)
</script>

<template>
  <div />
</template>
