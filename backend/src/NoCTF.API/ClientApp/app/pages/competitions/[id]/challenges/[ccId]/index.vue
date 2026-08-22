<script setup lang="ts">
const route = useRoute()
const competitionId = route.params.id as string
const competitionChallengeId = route.params.ccId as string

await navigateTo({
  path: `/competitions/${competitionId}/challenges`,
  query: { challenge: competitionChallengeId },
}, { replace: true })
</script>
