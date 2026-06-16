package com.provix.feature.preview

data class PreviewState(
    val path: String,
    val name: String,
    val textContent: String?,
    val extension: String,
)
