Pod::Spec.new do |s|
  s.name             = 'WakeOnLan'
  s.version          = '1.0.0'
  s.summary          = 'Native Wake-on-LAN UDP magic packet sender for Expo'
  s.description      = 'Native Wake-on-LAN UDP magic packet sender for Expo'
  s.license          = 'MIT'
  s.author           = 'Cerberus'
  s.homepage         = 'https://github.com/cgarst/cerberus'
  s.platforms        = { :ios => '15.1' }
  s.source           = { :git => '' }
  s.static_framework = true

  s.dependency 'ExpoModulesCore'

  s.source_files = '**/*.{h,m,swift}'
  s.pod_target_xcconfig = {
    'DEFINES_MODULE' => 'YES'
  }
end
