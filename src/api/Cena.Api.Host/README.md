# Cena.Api.Host (DEPRECATED)

> ⚠️ **DEPRECATED**: This host is being retired as part of DB-06. 
> 
> All endpoints have been migrated to:
> - **Cena.Student.Api.Host** — Student-facing endpoints
> - **Cena.Admin.Api.Host** — Admin-facing endpoints

## Migration Status (DB-06b)

| Endpoints | New Host | Status |
|-----------|----------|--------|
| MeEndpoints | Cena.Student.Api.Host | ✅ Migrated |
| SessionEndpoints | Cena.Student.Api.Host | ✅ Migrated |
| PlanEndpoints | Cena.Student.Api.Host | ✅ Migrated |
| GamificationEndpoints | Cena.Student.Api.Host | ✅ Migrated |
| TutorEndpoints | Cena.Student.Api.Host | ✅ Migrated |
| ChallengesEndpoints | Cena.Student.Api.Host | ✅ Migrated |
| SocialEndpoints | Cena.Student.Api.Host | ✅ Migrated |
| NotificationsEndpoints | Cena.Student.Api.Host | ✅ Migrated |
| KnowledgeEndpoints | Cena.Student.Api.Host | ✅ Migrated |
| StudentAnalyticsEndpoints | Cena.Student.Api.Host | ✅ Migrated |
| ClassroomEndpoints | Cena.Admin.Api.Host | ✅ Migrated |
| ContentEndpoints | Cena.Admin.Api.Host | ✅ Migrated |

## Next Steps (DB-06c)

1. Update `student-web` to use `Cena.Student.Api.Host` URL
2. Update `admin-web` to use `Cena.Admin.Api.Host` URL
3. Delete `Cena.Api.Host` project entirely

## History

This was the original monolithic API host. As part of DB-06 (Domain-Driven Design alignment), we've split it into separate bounded context hosts for better scalability and separation of concerns.
