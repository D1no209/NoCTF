<script setup lang="ts">
const route = useRoute()
const competitionId = route.params.id as string
</script>

<template>
  <NotificationCenter
    :competition-id="competitionId"
    heading="公告与通知"
    description="当前比赛的官方公告及与你有关的消息"
  />
</template>
