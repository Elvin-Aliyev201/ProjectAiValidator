# Project AI Validator (LookFuture AI)

Project AI Validator (və ya LookFuture AI) biznes ideyalarının bazar potensialını, rentabelliyini və rəqabət mühitini saniyələr ərzində təhlil edən **Süni İntellekt əsaslı (AI-powered)** bir platformadır. Layihə C# ASP.NET Core backend-i, müasir və dinamik HTML/CSS frontend-i, həmçinin Supabase (PostgreSQL) məlumat bazası istifadə edilərək yaradılmışdır.

## 🚀 Əsas Funksionallıqlar

*   **Süni İntellekt Analizi:** OpenRouter (Gemma-4-26b) modeli vasitəsilə biznes ideyalarının dərin təhlili.
*   **Ağıllı Panel (Dashboard):** Analiz nəticələrində TAM/SAM/SOM bazar həcmi, rəqiblərin boşluqları (feature gap), SWOT analizi, axtarış trendləri (qrafiklə) və GTM (Go-to-market) strategiyaları.
*   **Finans Kalkulyatoru:** Dinamik ARPU, CAC və Churn sürüşdürücüləri (sliders) ilə LTV və Break-even (qazanc nöqtəsi) hesablamaları.
*   **Təhlükəsiz Qeydiyyat Sistemi (JWT):** Yalnız istifadəçi adı və şifrə ilə çalışan, JWT (JSON Web Token) əsaslı yüngül və sürətli qeydiyyat/giriş sistemi. İmtahan üçün kodlanmış (BCrypt) şifrələr.
*   **Fərdi Tarixçə:** Hər bir istifadəçinin yalnız özünün etdiyi analizləri saxladığı və geri qaytara bildiyi tarixçə arxivi.
*   **Qlobal və Yerli Dəstək:** Bütün istifadəçi interfeysi və süni intellekt rəyləri tam Azərbaycan dilində (Localizaton) təqdim olunur.

## 🛠 İstifade Olunan Texnologiyalar

**Backend:**
*   C# / ASP.NET Core 8.0 Web API
*   JWT Authentication (Microsoft.AspNetCore.Authentication.JwtBearer)
*   BCrypt.Net-Next (Şifrələmə üçün)
*   Polly (API sorğularında Rate-Limit xətalarına qarşı təkrar cəhdlər üçün)

**Frontend:**
*   Vanilla HTML, CSS, JavaScript (Heç bir ağır framework istifadə edilmədən)
*   Glassmorphism, mürəkkəb qrafiklər və SVG animasiyalar üçün xüsusi CSS dizayn
*   TailwindCSS rəng palitrasına bənzər fərdi CSS dəyişənləri

**Verilənlər Bazası və AI:**
*   Supabase (PostgreSQL) - User və Idea datalarının saxlanması üçün REST API
*   OpenRouter - `google/gemma-4-26b-a4b-it:free` LLM modeli ilə API inteqrasiyası

## ⚙️ Qurulum və İşə Salma

### 1. Repozitoriyanı klonlayın
```bash
git clone https://github.com/Elvin-Aliyev201/ProjectAiValidator.git
cd ProjectAiValidator/LookFutureApi
```

### 2. Tələblər
*   .NET 8 SDK
*   Supabase hesabı və layihəsi
*   OpenRouter API Açarı

### 3. Konfiqurasiya
`appsettings.json` faylında öz API açarlarınızı qeyd edin:
```json
{
  "OpenRouter": {
    "ApiKey": "SİZİN_OPENROUTER_AÇARINIZ",
    "Model": "google/gemma-4-26b-a4b-it:free",
    "BaseUrl": "https://openrouter.ai/api/v1/chat/completions"
  },
  "Supabase": {
    "Url": "SİZİN_SUPABASE_URL_İNİZ",
    "ApiKey": "SİZİN_SUPABASE_AÇARINIZ"
  },
  "Jwt": {
    "Key": "Məxfi_JWT_Açarı"
  }
}
```

### 4. Supabase Verilənlər Bazasını Hazırlayın
Supabase SQL Editor-da aşağıdakı skripti işlədin:
```sql
create table if not exists users (
  id uuid default gen_random_uuid() primary key,
  created_at timestamp with time zone default timezone('utc'::text, now()) not null,
  username text unique not null,
  password_hash text not null
);

create table if not exists ideas (
    id uuid default gen_random_uuid() primary key,
    created_at timestamp with time zone default timezone('utc'::text, now()) not null,
    title text,
    raw_description text,
    business_type text,
    viability_score integer,
    tam text,
    sam text,
    som text,
    analysis_json text,
    user_id uuid references users(id) on delete set null
);
```

### 5. Layihəni İşə Salın
```bash
dotnet run
```
Sistem `http://localhost:5000` ünvanında işləyəcək.

## 📄 Lisenziya
Bu layihə Elvin Aliyev tərəfindən hazırlanmışdır. Bütün hüquqlar qorunur.
