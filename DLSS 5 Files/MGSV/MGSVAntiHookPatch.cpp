#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <bcrypt.h>

#include <array>
#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <string>
#include <vector>

#pragma comment(lib, "bcrypt.lib")

namespace {
constexpr std::uint64_t kExpectedSize = 166517760;
constexpr std::uint64_t kPatchOffset = 0x2B90AB;
constexpr std::array<unsigned char, 2> kOriginalBytes{0x75, 0x2D};
constexpr std::array<unsigned char, 2> kPatchedBytes{0xEB, 0x2D};
constexpr wchar_t kOriginalSha256[] =
    L"085C2F82D1C963C40B3D2D55786661DFEE2B18CBBF388A710C00FA76C5E9BB45";
constexpr wchar_t kPatchedSha256[] =
    L"184E0D1ABEC30561EEE4650CB7F913E838692BA30233E8AAB5DCBCE522D8C297";

std::wstring Sha256(const std::filesystem::path &path) {
  BCRYPT_ALG_HANDLE algorithm = nullptr;
  BCRYPT_HASH_HANDLE hash = nullptr;
  DWORD object_size = 0, result_size = 0;
  std::vector<unsigned char> object;
  std::array<unsigned char, 32> digest{};

  if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0 ||
      BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH,
                        reinterpret_cast<PUCHAR>(&object_size), sizeof(object_size),
                        &result_size, 0) < 0) {
    if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
    return {};
  }

  object.resize(object_size);
  if (BCryptCreateHash(algorithm, &hash, object.data(), object_size, nullptr, 0, 0) < 0) {
    BCryptCloseAlgorithmProvider(algorithm, 0);
    return {};
  }

  FILE *file = nullptr;
  if (_wfopen_s(&file, path.c_str(), L"rb") != 0 || file == nullptr) {
    BCryptDestroyHash(hash);
    BCryptCloseAlgorithmProvider(algorithm, 0);
    return {};
  }

  std::vector<unsigned char> buffer(1 << 20);
  while (const size_t count = fread(buffer.data(), 1, buffer.size(), file)) {
    if (BCryptHashData(hash, buffer.data(), static_cast<ULONG>(count), 0) < 0) {
      fclose(file);
      BCryptDestroyHash(hash);
      BCryptCloseAlgorithmProvider(algorithm, 0);
      return {};
    }
  }
  const bool read_ok = !ferror(file);
  fclose(file);

  const bool finish_ok = read_ok &&
      BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0) >= 0;
  BCryptDestroyHash(hash);
  BCryptCloseAlgorithmProvider(algorithm, 0);
  if (!finish_ok) return {};

  wchar_t text[65]{};
  for (size_t i = 0; i < digest.size(); ++i)
    swprintf_s(text + i * 2, 3, L"%02X", digest[i]);
  return text;
}

void Pause() {
  std::puts("\nPress Enter to close.");
  (void)getchar();
}

int Fail(const char *message) {
  std::printf("ERROR: %s\n", message);
  Pause();
  return 1;
}
}  // namespace

int wmain(int argc, wchar_t **argv) {
  std::puts("MGSV ReShade Anti-Hook Patcher v1.0");
  std::puts("Target: MGSV: The Phantom Pain 1.0.15.4 (English, Steam)\n");

  std::filesystem::path target;
  if (argc >= 2) {
    target = argv[1];
  } else {
    wchar_t module_path[MAX_PATH]{};
    if (!GetModuleFileNameW(nullptr, module_path, MAX_PATH))
      return Fail("Could not determine the patcher location.");
    target = std::filesystem::path(module_path).parent_path() / L"mgsvtpp.exe";
  }

  std::error_code ec;
  if (!std::filesystem::is_regular_file(target, ec))
    return Fail("mgsvtpp.exe was not found. Put this patcher next to mgsvtpp.exe.");
  if (std::filesystem::file_size(target, ec) != kExpectedSize)
    return Fail("Unsupported mgsvtpp.exe size. No changes were made.");

  const std::wstring before_hash = Sha256(target);
  if (before_hash.empty())
    return Fail("Could not read or hash mgsvtpp.exe. Make sure the game is closed.");
  if (before_hash == kPatchedSha256) {
    std::puts("This executable is already patched. No changes were made.");
    Pause();
    return 0;
  }
  if (before_hash != kOriginalSha256)
    return Fail("Unsupported or modified mgsvtpp.exe hash. No changes were made.");

  FILE *file = nullptr;
  if (_wfopen_s(&file, target.c_str(), L"rb") != 0 || file == nullptr)
    return Fail("Could not open mgsvtpp.exe for verification.");
  if (_fseeki64(file, kPatchOffset, SEEK_SET) != 0) {
    fclose(file);
    return Fail("Could not seek to the patch location.");
  }
  std::array<unsigned char, 2> bytes{};
  const bool bytes_ok = fread(bytes.data(), 1, bytes.size(), file) == bytes.size();
  fclose(file);
  if (!bytes_ok || bytes != kOriginalBytes)
    return Fail("Unexpected bytes at the patch location. No changes were made.");

  const auto backup = target.parent_path() / L"mgsvtpp.exe.anti-hook-backup";
  if (std::filesystem::exists(backup, ec)) {
    if (Sha256(backup) != kOriginalSha256)
      return Fail("A backup already exists but has an unexpected hash. No changes were made.");
  } else if (!std::filesystem::copy_file(target, backup,
                                         std::filesystem::copy_options::none, ec)) {
    return Fail("Could not create mgsvtpp.exe.anti-hook-backup.");
  }

  if (_wfopen_s(&file, target.c_str(), L"r+b") != 0 || file == nullptr)
    return Fail("Could not open mgsvtpp.exe for writing. Close the game and try again.");
  bool write_ok = _fseeki64(file, kPatchOffset, SEEK_SET) == 0 &&
                  fwrite(kPatchedBytes.data(), 1, kPatchedBytes.size(), file) ==
                      kPatchedBytes.size() &&
                  fflush(file) == 0;
  fclose(file);
  if (!write_ok) return Fail("Writing the patch failed. Restore the backup before launching.");

  if (Sha256(target) != kPatchedSha256) {
    std::filesystem::copy_file(backup, target,
                               std::filesystem::copy_options::overwrite_existing, ec);
    return Fail("Post-patch verification failed. The original backup was restored.");
  }

  std::puts("SUCCESS: FOX Engine's D3D11 anti-hook check was bypassed.");
  std::puts("Backup: mgsvtpp.exe.anti-hook-backup");
  std::puts("You can now install ReShade as dxgi.dll and launch the game.");
  Pause();
  return 0;
}
