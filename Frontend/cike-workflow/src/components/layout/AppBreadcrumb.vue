<template>
  <Breadcrumb>
    <BreadcrumbList>
      <template v-for="(item, index) in items" :key="index">
        <BreadcrumbItem>
          <BreadcrumbLink v-if="item.to" as-child>
            <RouterLink :to="item.to">{{ item.label }}</RouterLink>
          </BreadcrumbLink>
          <BreadcrumbPage v-else>{{ item.label }}</BreadcrumbPage>
        </BreadcrumbItem>
        <BreadcrumbSeparator v-if="index < items.length - 1" />
      </template>
    </BreadcrumbList>
  </Breadcrumb>
</template>

<script setup lang="ts">
import { RouterLink } from "vue-router"
import {
  Breadcrumb,
  BreadcrumbList,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb"
import { useBreadcrumbs } from "@/composables/useBreadcrumbs"

const { items } = useBreadcrumbs()
</script>
