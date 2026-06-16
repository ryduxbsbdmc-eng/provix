pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "Provix"

include(":app")
include(":core:ui")
include(":core:model")
include(":core:filesystem")
include(":core:settings")
include(":core:localization")
include(":feature:browser")
include(":feature:preview")
include(":feature:archive")
include(":feature:settings")
