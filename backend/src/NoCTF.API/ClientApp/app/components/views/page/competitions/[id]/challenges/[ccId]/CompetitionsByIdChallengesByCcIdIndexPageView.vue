<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdChallengesByCcIdIndexPageViewState } from '~/features/routes/competitions/[id]/challenges/[ccId]/useCompetitionsByIdChallengesByCcIdIndexPage'

const viewProps = defineProps<{ state: CompetitionsByIdChallengesByCcIdIndexPageViewState }>()
const {  } = toRefs(viewProps.state)
</script>

<template><div /></template>
