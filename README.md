# GetSMSZTE
Authentication methods for ZTE Routers and SMS retrieving

## Disclaimer
**Note**: This code is provided as-is for informational purposes only. No technical support or guarantee of operation is offered.

## Overview
ZTE routers implement three distinct methods to encode passwords for authentication purposes. The more sophisticated methods (1 and 2) probably act as a security mechanism to compensate for the use of HTTP instead of HTTPS in the router's web interface.

The specific authentication method is determined by the `WEB_ATTR_IF_SUPPORT_SHA256` value, which is hardcoded into the router's firmware.

## Method 1: SHA256 with Base64 (WEB_ATTR_IF_SUPPORT_SHA256 = 1)
- Takes the plaintext password
- Encodes it in Base64
- Computes the SHA256 hash of the Base64 string
- Status: Not tested
- Use case: Older router models
- Note: Offers moderate security but is less efficient than Method 2

## Method 2: Double SHA256 with LD Value (WEB_ATTR_IF_SUPPORT_SHA256 = 2)
- Takes the plaintext password
- Computes the first SHA256 hash of the password
- Concatenates the resulting hash with the LD value (a server-generated timestamp-based challenge)
- Computes a second SHA256 hash of the concatenated string
- Status: Tested and confirmed working on the MC888 router model
- Use case: Current generation routers
- Note: This is the most secure method implemented by ZTE routers

## Method 3: Simple Base64 (WEB_ATTR_IF_SUPPORT_SHA256 = any other value)
- Takes the plaintext password
- Encodes it using simple Base64 encoding
- Status: Not tested
- Use case: Legacy router models
- Note: This is the least secure method and is only used for backward compatibility

## Important Notes
1. The `WEB_ATTR_IF_SUPPORT_SHA256` value is embedded in the router's firmware and cannot be modified without a firmware update
2. The information about these methods was obtained through reverse engineering of the web interface, specifically its JavaScript code on my ZTE MC888 device

## Class Description
This program implements a client for ZTE router authentication and SMS management. It handles login procedures, SMS retrieval, and tracks messages' status using the Windows Registry for detecting changes in the SMS list.
