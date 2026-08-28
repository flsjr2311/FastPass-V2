# Regras ProGuard. Moshi/Retrofit funcionam com reflexão nos modelos de dados.
-keep class com.fastpass.validator.data.model.** { *; }
-keepclassmembers class ** {
    @com.squareup.moshi.Json <fields>;
}
