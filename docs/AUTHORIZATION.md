# SystemPanel — Yetkilendirme

Üç katman birlikte çalışır:

1. **Kimlik** — kullanıcı kim? (AD veya yerel Identity hesabı)
2. **İzin** — bu tür bir işlemi yapabilir mi? (`ad.users.password.reset`)
3. **Kapsam** — bu *hedef nesne* üzerinde yapabilir mi? (yalnızca kendi ilinin OU'ları)

İzin kontrolü controller'da `[RequirePermission]` ile, kapsam kontrolü servis katmanında (`IAdUserOperations`, `AdComputerOperations`) yapılır. UI'da buton gizlemek yalnızca kullanılabilirlik içindir, yetki kontrolünün yerine geçmez.

## 1. Rol eşleme (AD grubu → rol)

Girişte `LoginService` kullanıcının AD grup üyeliklerini (`UserPrincipal.GetGroups`, sAMAccountName ve ad) okur ve `RoleMapping` ile rollere çevirir:

```json
"RoleMapping": {
  "Admin":      [ "<AD grubu>" ],
  "LocalAdmin": [ "<AD grubu>", "<AD grubu>" ],
  "Operator":   [],
  "Helpdesk":   [],
  "Monitoring": []
}
```

- Anahtarlar rol adlarıdır; değerler AD grup adlarıdır (büyük/küçük harf duyarsız).
- **Hiçbir role eşlenmeyen kullanıcı giriş yapamaz** (önceden varsayılan rol atanıyordu). Bu kırıcı bir değişikliktir: geçişten önce tüm kullanıcı gruplarının eşlendiğinden emin olun.
- Grup adları koda gömülü değildir.

## 2. İzin kataloğu

| İzin | Açıklama |
|---|---|
| `dashboard.view` / `dashboard.manage` | Ana sayfa; NOC panel yönetimi |
| `servers.view` / `servers.manage` / `servers.services.control` | Sunucu listesi; düzenleme; servis başlat/durdur |
| `ad.users.view` | AD kullanıcı arama/detay |
| `ad.users.create` / `.edit` / `.disable` / `.delete` | Oluşturma, düzenleme, etkinleştirme/devre dışı, silme |
| `ad.users.password.reset` / `ad.users.unlock` / `ad.users.move` | Parola sıfırlama, kilit açma, OU taşıma |
| `ad.groups.manage` | Grup üyeliği ekleme/çıkarma |
| `ad.computers.view` / `ad.computers.manage` | Bilgisayar listesi/detay; yönetim |
| `laps.read` | Tek bilgisayar için LAPS parolası gösterme (audit'lenir) |
| `gpo.view` / `.create` / `.edit` / `.link` / `.backup` / `.delete` | GPO işlemleri (silme/içe aktarma ayrıca yapılandırmayla açılmalı) |
| `sccm.view` / `sccm.manage` | SCCM DP görünümü; allowlist'teki servisleri kontrol |
| `files.view` / `files.upload` / `files.delete` | Dosya yönetimi |
| `announcements.view` / `announcements.manage` | Duyurular |
| `monitoring.view` / `monitoring.manage` | Metrik/alarm görüntüleme; agent kaydı ve anahtar yönetimi |
| `sms.send` / `sms.bulk` | Tekil SMS; toplu SMS |
| `audit.view` | Denetim kayıtları |
| `logs.view` / `logs.manage` | Uygulama logları; temizleme |
| `settings.view` / `settings.manage` | Sağlık durumu (`/health/ready`), ayarlar; rol-izin matrisi |
| `users.manage` | Yerel Identity kullanıcıları |
| `scope.all` | **Organizasyonel kapsam sınırı yok** (tüm iller, ayrıcalıklı hesaplar, korumalı gruplar) |

Kaynak: `src/SystemPanel/Security/Permissions.cs`. Yeni izin eklemek için oraya sabit ekleyin, `All` listesine dahil edin ve gerekiyorsa `DefaultRolePermissions` güncelleyin.

## 3. Varsayılan rol → izin eşlemesi

`RolePermissions` tablosu boşsa ilk açılışta `DefaultRolePermissions` yazılır. Sonrasında **İzinler** ekranından (`settings.manage`) değiştirilir; değişiklikler audit'lenir.

| Rol | İzinler |
|---|---|
| Admin | Tümü (`scope.all` dahil) |
| LocalAdmin | dashboard.view, servers.view/manage, ad.users.view/edit/disable/password.reset/unlock/move, ad.groups.manage, ad.computers.view, laps.read, sccm.view/manage, announcements.view, monitoring.view |
| Operator | dashboard.view, ad.users.view/edit/disable/password.reset/unlock/move, ad.groups.manage |
| Helpdesk | Operator + ad.users.create |
| Monitoring | dashboard.view, monitoring.view |

Notlar:
- GPO, SMS, loglar, servis kontrolü ve dashboard yönetimi varsayılan olarak yalnızca Admin'dedir.
- LocalAdmin'in SCCM servis kontrolü kendi kapsamıyla sınırlı değildir (DP-il eşlemesi yok). Bu nedenle servis kontrolü ayrıca `scope.all` ister. LocalAdmin'ler varsayılan olarak servis kontrolü yapamaz.

## 4. Organizasyonel kapsam

"Birim" kurumun organizasyon yapısındaki bir düğümdür (il, bölge, şube, kampüs...). Arayüzdeki adı `Organization:UnitLabel` ile belirlenir. Aşağıdaki örnekler il yapısını kullanır.

`OrganizationalScope` = kullanıcının işlem yapabileceği OU kökleri listesi.

- `scope.all` izni → sınırsız.
- Aksi halde `ou:root` claim'leri → yalnızca bu OU'lar ve altındaki nesneler.
- Hiç kök yoksa → **hiçbir nesne** (fail-closed).

Bir DN'in kapsamda olup olmadığı RFC 4514 ayrıştırması ile (kaçış karakterleri, büyük/küçük harf, boşluklar) RDN dizisinin sonek eşleşmesi olarak hesaplanır. Dize `EndsWith` karşılaştırması kullanılmaz (`OU=XANKARA` ≠ `OU=ANKARA`).

### Kapsamın türetilmesi (`ScopeDerivation`)

Giriş sırasında `CustomUserClaimsPrincipalFactory`, kullanıcının kendi DN'inden il ve kapsam köklerini üretir. Yapılandırma: `ActiveDirectory:Scope`.

```
AnchorOu  = "IL"
BranchOus = ["KULLANICILAR", "BILGISAYARLAR"]

Kullanıcı DN: CN=Ali,OU=KULLANICILAR,OU=ANKARA,OU=IL,DC=corp
  il             = ANKARA
  kapsam kökleri = OU=ANKARA,OU=IL,DC=corp
                   OU=ANKARA,OU=KULLANICILAR,OU=IL,DC=corp
                   OU=ANKARA,OU=BILGISAYARLAR,OU=IL,DC=corp
```

- `AnchorOu` boşsa veya DN anchor içermiyorsa kapsam türetilmez → kullanıcı (scope.all yoksa) hiçbir AD nesnesine erişemez.
- `LocalAdminGroupSuffix` (ör. `_Local_Admins`) ile biten gruplar `sp:province` claim'i için kullanılır. `ExcludedLocalAdminGroups` il vermez.

### Ek kurallar

- `adminCount=1` (AdminSDHolder korumalı) hesaplar ve `Domain Admins`, `Enterprise Admins`, `Schema Admins`, `Administrators` vb. + `ActiveDirectory:ProtectedGroups` → yalnızca `scope.all`.
- Taşıma (`move`) işleminde hem kaynak hem hedef OU kapsamda olmalıdır.
- Grup üyeliği işleminde hem kullanıcı hem grup kapsamda olmalıdır (veya `scope.all`).
- Reddedilen her deneme `authz.denied` olarak audit'lenir.

## 5. Kurulum kontrol listesi

1. `RoleMapping` grup adlarını kurumunuzun AD gruplarıyla doldurun.
2. `ActiveDirectory:Scope` değerlerini OU yapınıza göre girin. Bir test LocalAdmin hesabıyla giriş yapın. Kendi ilindeki kullanıcıların listelendiğini ve başka ildeki bir kullanıcıya erişimin reddedildiğini (`authz.denied` audit kaydı) doğrulayın.
3. İlk açılışta `RolePermissions` tohumlanır. Matris ekranından gözden geçirin.
4. Eşlenmemiş kullanıcıların girişi reddedilir; giriş hatalarını Denetim Kayıtları'ndan (`auth.login.failure`) izleyin.
