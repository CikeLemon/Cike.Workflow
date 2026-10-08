<template>
  <div v-if="blank" class="h-screen overflow-hidden">
    <RouterView />
  </div>
  <AppLayout v-else>
    <RouterView />
  </AppLayout>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { useRoute } from "vue-router"
import AppLayout from "@/layouts/AppLayout.vue"

const route = useRoute()
// Full-screen routes (e.g. the workflow designer) opt out of the app shell.
const blank = computed(() => route.meta.layout === "blank")
</script>
