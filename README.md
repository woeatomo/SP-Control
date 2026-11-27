# Spare Part Control

Aplikasi kontrol stok spare part untuk tim maintenance — tanpa jual-beli; transaksinya adalah part **dipakai (Issued)**, **masuk (Input)** dan **dipinjam (Loan)**.

**Stack:** ASP.NET Core 8 (Razor Pages) · Entity Framework Core · SQLite · Bootstrap 5 · QRCoder · html5-qrcode · Chart.js (semua library tersimpan lokal, bisa jalan di intranet tanpa internet).

## Menjalankan

```bash
dotnet run
```

Buka `http://localhost:5000` (atau alamat yang tampil di konsol). Database `sparepart.db` dibuat otomatis saat pertama jalan.

- Data contoh (12 part) diisi otomatis bila database kosong. Matikan dengan `"SeedSampleData": false` di `appsettings.json`, lalu hapus `sparepart.db` untuk mulai dari kosong.
- Akses dari HP/tablet di jaringan yang sama: `dotnet run --urls http://0.0.0.0:5000`.
- **Kamera untuk scan QR hanya diizinkan browser di HTTPS atau localhost.** Scanner USB/Bluetooth (mode keyboard) bekerja tanpa syarat itu.

## Login

Semua halaman wajib login. Akun awal dibuat otomatis saat database pertama kali dibuat:

| Username | Password | Role |
|---|---|---|
| `admin` | `admin123` | Admin — transaksi, ubah master data, kurs, stock checking, kelola user |
| `viewer` | `viewer123` | Viewer — hanya melihat, mencari, export CSV, cetak label QR |
| `tablet` | `tablet123` | Peminta (tablet) — hanya halaman permintaan part, tanpa harga |

**Segera ganti kedua password ini** (klik nama user di kanan atas → Ganti Password, atau menu **User** → Reset). User baru, ubah role dan nonaktifkan akun ada di menu **User** (khusus Admin).

## Struktur master data (sama dengan Excel "00. MASTER STOCK APP")

| Kolom Excel | Di aplikasi | Keterangan |
|---|---|---|
| goodsCode | Goods Code | Isi QR. Format diatur di menu **Pengaturan → Perusahaan & Format Kode** (default `MaPC-MAKER-0001`). Part 2nd used = kode + akhiran 2nd (default `-2ND`) |
| goodsName, Model, rackName, makerName, suppName, Remark | sama | |
| stockCategory | ACTIVE / INACTIVE | dipakai untuk memisah nilai aset |
| currency, unit_price | sama | USD / SGD / JPY / IDR |
| unit_USD | dihitung | `unit_price × Rate_to_USD` (menu **Kurs**) |
| initialStock, minStock, maxStock | sama | |
| nowStock | stok sistem | hanya berubah lewat transaksi |
| Status | dihitung | **EMPTY** = 0 · **LOW** = di bawah min · **CRITICAL** = sama dengan min · **OVERSTOCK** = di atas max · selain itu **NORMAL** |
| StockValue | dihitung | `nowStock × unit_USD` |

**Import langsung dari Excel**: menu Master Data → **Import Excel** → upload `00. MASTER STOCK APP.xlsm` (atau .xlsx/.csv) → **Cek file** (belum menyimpan, menampilkan kolom yang dikenali dan baris bermasalah) → **Simpan import**. Kurs ikut diambil dari tabel `tblRate` bila ada. Goods code baru → `nowStock` jadi saldo awal; yang sudah ada → data master diperbarui (stok hanya disamakan bila opsi *samakan stok* dicentang, selisihnya tercatat sebagai Adjustment). **Export Excel** menghasilkan file dengan susunan kolom yang sama (+ sheet RATE), sehingga bisa diimport ulang.

## Permintaan part dari tablet

Untuk ruang sparepart yang terpisah dari ruang produksi / maintenance:

- **Tablet di ruang produksi** login dengan akun berperan **Peminta (tablet)** (dibuat di menu User, tetap login 30 hari) dan langsung terbuka di halaman **/Request**: layar sentuh dengan katalog part (foto, nama, status *Tersedia / Stok terbatas / Stok habis*, tanpa harga), scan QR, dan kolom *"Tidak menemukan part-nya? Tulis saja"*. Isi **nama PIC yang meminta (wajib)** dan **digunakan untuk dimana (opsional)**, pilih urgensi → **Kirim**. Detail lain (no. WO, dll.) dilengkapi PIC spare part di form Issued. Tombol **Status permintaan** menampilkan proses (Menunggu → Sedang disiapkan → Siap diambil → Selesai / Ditolak) dan tablet berbunyi saat permintaan siap diambil.
- **PC ruang sparepart (admin)** mengecek permintaan baru setiap 5 detik: **bunyi notifikasi** (nada "pesan masuk" yang disintesis di browser), badge di menu **Permintaan**, judul tab berkedip, dan **popup** permintaan. Part dari katalog langsung tampil dengan stok, rack dan tombol Location; permintaan berupa teks otomatis dicocokkan ke katalog (termasuk istilah Indonesia → Inggris, mis. *oli → oil, sabuk → belt*) dan ditampilkan sebagai pilihan — bila tidak ada yang cocok, hanya teks permintaannya yang tampil dengan tombol **Cari manual**.
- Tombol di popup: **Proses**, **Siap diambil**, **Tolak** (alasan tampil di tablet), **Buat Issued** (form Issued terisi otomatis: PIC, line, no. permintaan, part; setelah disimpan permintaan otomatis *Selesai*).
- Browser baru mengizinkan suara setelah ada satu klik di halaman; klik **🔔 Suara aktif — tes** di halaman Permintaan untuk memastikan. Notifikasi desktop Windows juga muncul bila browser diizinkan (perlu HTTPS atau localhost).

## Multi site / section

Satu aplikasi bisa dipakai beberapa site / section spare part control (mis. Plant 1 Injection, Plant 2 Press, Gudang Pusat):

- **Data terpisah per site**: master part, stok, transaksi, loan, stock checking dan import. Goods code boleh sama di site berbeda.
- **Pengaturan → Site / Section**: tambah site (kode singkat, nama, prefix goods code khusus site), nonaktifkan, atau hapus site yang belum berisi data.
- **Akses user** (menu User): *Semua site* atau site tertentu.
  - Admin + semua site = pengelola sistem (site, user, kurs, format kode).
  - Admin site tertentu = admin section: transaksi & master data hanya di site-nya.
  - Viewer = hanya melihat site yang diberikan.
- **Pemilih site** di menu atas (bila user punya lebih dari satu site), termasuk **Semua site** untuk dashboard ringkasan per site, report gabungan (Stock Value per Site) dan master gabungan. Transaksi/import selalu di satu site; membuka Issue dari part tertentu otomatis pindah ke site part tersebut.
- Nomor dokumen memuat kode site: `ISS-PC1-261120-001`, `LOAN-PC2-…`, `SC-PC1-2026-S2`. Token `{SITE}` bisa dipakai di format goods code; label QR menampilkan kode site.
- **Kurs, format kode dan nama perusahaan berlaku untuk semua site.**
- **Upgrade dari versi sebelumnya**: semua data lama otomatis masuk ke site pertama "Site Utama" (kode `SITE1`) — ganti nama/kodenya di Pengaturan → Site / Section. User lama tetap punya akses semua site.

## Pengaturan (untuk dipakai di berbagai perusahaan)

Menu **Pengaturan → Perusahaan & Format Kode** (khusus Admin, tersimpan di database):

- **Nama perusahaan & judul aplikasi** — tampil di menu atas, halaman login, dan judul report saat dicetak.
- **Pola goods code** dengan token `{PREFIX}` `{MAKER}` `{MODEL}` `{SUPP}` `{YYYY}` `{YY}` `{MM}` `{SEQ}`. Contoh: `{PREFIX}-{MAKER}-{SEQ}` → `MaPC-BANDO-0001`, `SP-{MODEL}-{SEQ}` → `SP-INJECTION-0001`, `{PREFIX}{YY}{MM}{SEQ}` → `PT26110001`.
- **Nomor urut**: jumlah digit, dan dihitung per kombinasi (per maker/model/bulan) atau satu nomor untuk semua part.
- **Isi segmen**: huruf besar, hapus spasi & simbol, batas panjang (mis. 4 → `SIND`).
- **Akhiran part 2nd used** (default `-2ND`); bila diganti, goods code part 2nd yang sudah ada bisa ikut diganti.
- **Goods code kosong** saat simpan part baru / import Excel → dibuat otomatis.
- Pratinjau langsung memakai data part yang ada, sebelum disimpan.

## Fitur

| Menu | Isi |
|---|---|
| **Dashboard** | Notifikasi EMPTY / LOW / CRITICAL untuk semua part (active maupun inactive) dengan saran order (max − now), jumlah OVERSTOCK, loan terlambat, pengingat stock checking semester berjalan, nilai aset aktif & non-aktif (USD), grafik nilai pemakaian per bulan, transaksi terakhir |
| **Cari Part** | Pencarian cepat untuk teknisi: hasil langsung saat mengetik berupa kartu (foto, stok + status, rack, tombol Location/Issue/Label), beberapa kata sekaligus (`bando 1440`), tidak peka tanda baca (`s8m1440` = `S8M-1440`), diurutkan menurut relevansi, filter cepat (ada stok, perlu restock, reusable, 2nd, model), scan QR, pencarian terakhir. Kotak cari di menu atas juga langsung menampilkan hasil (tekan `/` dari halaman mana pun) |
| **Master Data** | Kolom supplier dan lokasi penyimpanan; gambar part (opsional, disarankan); popup detail dengan gambar dan tombol 📍 Location; kolom sama dengan Excel MASTER STOCK; pencarian global + filter di setiap header kolom (termasuk Status), sorting, export/import CSV, cetak label QR, tanda ♻ Reusable dan kondisi 2nd, status aset Aktif / Non-aktif |
| **Label QR** | Pembuat label untuk dicetak dan ditempel: Kecil 40×20 mm (di part), Sedang 60×30 mm (wadah/kantong), Besar 100×50 mm (drawer/box/rak); jumlah salinan per part; tambah part dengan scan/ketik |
| **Issued** | Nama PIC maintenance peminta, mesin/line, no. WO; scan QR berulang = qty bertambah; stok tidak bisa minus |
| **Input** | Stok masuk (pembelian, atau part bekas/2nd yang dikembalikan); setelah disimpan muncul tombol cetak label QR untuk barang yang baru datang (satu per part, atau satu per qty) |
| **Loan** | Peminjaman dengan rencana tanggal kembali; saat kembali bisa dipisah: dikembalikan ke stok / terpakai (otomatis jadi Issued) |
| **Riwayat** | Kartu stok semua transaksi, filter tanggal/tipe/part/PIC, export CSV |
| **Stock Checking** | Satu sesi per semester (S1 Jan–Jun, S2 Jul–Des); scan QR → isi hasil hitung → Enter; selisih bisa diposting sebagai adjustment; record tiap periode tersimpan |
| **Report** | Stock value per Model / Maker / Supplier / mata uang (active vs inactive), Perlu Restock + saran order & estimasi biaya, OVERSTOCK, pemakaian per part / PIC / mesin, part habis & minimum, loan belum kembali, part reusable & 2nd — semua bisa export CSV / cetak |
| **Kurs** | Kurs SGD, JPY, IDR — isi sebagai "1 USD = …" atau Rate_to_USD seperti tblRate; saat disimpan kolom Harga USD di master data dihitung ulang |

## Catatan desain

- **Harga**: disimpan dalam mata uang asli (USD/SGD/JPY/IDR) + kolom `UnitPriceUsd` hasil konversi. Setiap transaksi menyimpan harga USD saat itu, jadi perubahan kurs tidak mengubah nilai transaksi lama.
- **Stok** hanya berubah lewat transaksi (`Services/StockService.cs`), tidak bisa diedit langsung di master data.
- **Isi QR** = part number (teks polos), sehingga terbaca oleh scanner apa pun.
- **Part 2nd used**: part number = part number original + akhiran 2nd (default `-2ND`, diatur di Pengaturan) (mis. `BRG-6205-ZZ-2ND`). Akhiran ditambahkan otomatis bila kondisi dipilih *2nd*, dan sebaliknya part number berakhiran `-2ND` otomatis berkondisi 2nd. Tombol **+ Part 2nd** di halaman detail menyalin data part original. Part original dan versi 2nd-nya saling tertaut di halaman detail.
- **Gambar part**: disimpan di folder `App_Data/part-images` (bisa diubah lewat `"ImageFolder"` di `appsettings.json`), bukan di database — **ikutkan folder ini saat backup** bersama `sparepart.db`. Foto besar dikecilkan otomatis di browser (maks. 1280 px) sebelum diupload. Gambar hanya bisa dibuka setelah login.
- **Cetak label**: pilih skala **100% / Actual size** di dialog print agar ukuran label tepat. Kertas stiker A4 memuat 2 kolom label besar; garis putus-putus adalah garis potong.
- **Login**: cookie authentication dengan tabel `Users` sendiri (password di-hash PBKDF2). Setiap transaksi mencatat username penginput.
- **Tombol Location (persiapan versi berikutnya)**: saat ini lokasi rak berkedip di layar. Untuk menyalakan lampu / LCD di rak, buat class yang mengimplementasikan `IRackLocator` (`Services/RackLocator.cs`) — misalnya mengirim HTTP/MQTT ke kontroler rak — lalu daftarkan di `Program.cs` menggantikan `NullRackLocator`. Halaman dan API (`POST /api/parts/{id}/locate`) tidak perlu diubah.

## Pindah ke SQL Server

1. `dotnet add package Microsoft.EntityFrameworkCore.SqlServer`
2. Di `Program.cs` ganti `UseSqlite(...)` menjadi `UseSqlServer(...)` dan isi connection string di `appsettings.json`.
3. Di `Data/AppDbContext.cs` hapus konversi `decimal → double` dan collation `NOCASE`.
4. Hapus folder `Migrations`, lalu `dotnet tool restore && dotnet ef migrations add InitialCreate`.

## Struktur

```
Models/Entities.cs        Site, UserSite, AppUser, Part, StockTransaction, Loan, StockCheckSession, ExchangeRate
Data/                     AppDbContext, SeedData
Services/                 SiteContext (site aktif + filter data per site), StockService (logika stok), MasterImport + ExcelMaster (import/export Excel), PartCode, PartFilter, Csv, RackLocator (hook lampu/LCD rak)
Pages/                    Account (login, pilih site), Users, Settings (pengaturan, site, kurs), Dashboard, Parts, Entry (Issued/Input/Loan), Loans, Transactions, StockChecks, Reports, Settings
Program.cs                Konfigurasi + API /api/parts/lookup, /api/parts/search, /qr
```
