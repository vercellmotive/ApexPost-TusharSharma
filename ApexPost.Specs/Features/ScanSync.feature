@service @device
Feature: Scan sync from the courier's device
  Couriers record scans on their device, often with no signal. When the device syncs, the outbox
  uploads each pending scan to the server. The server stores each scan once, however many times
  it arrives, and refuses a scan that breaks a rule, with a reason the courier can read.

  These scenarios run at service level: the Part A outbox on a real Couchbase Lite database,
  wired in process to the real shipment service. HTTP adds nothing to these rules.

  Background:
    Given shipment "APX-50000001" is out for delivery
    And courier "CR-1001" is signed in on the device

  Scenario: Scans saved offline are uploaded when the device syncs
    Given the courier saved these scans while offline:
      | scan type | location       | minutes after pickup |
      | Pickup    | Sydney depot   | 0                    |
      | InTransit | Parramatta hub | 25                   |
    When the device syncs
    Then 2 scans are synced on the device
    And the server holds 2 scans for "APX-50000001"

  Scenario: A scan the server already has is skipped, not stored twice
    Given the courier saved a "Pickup" scan at "Sydney depot" while offline
    And the device has synced
    And the reply was lost, so the device still shows that scan as pending
    When the device syncs
    Then the scan is synced on the device
    And the server holds 1 scan for "APX-50000001"

  Scenario: A Delivered scan without a signature is rejected
    Given the courier saved a "Delivered" scan at "Front door" with no signature while offline
    When the device syncs
    Then the scan is marked failed on the device because "A Delivered scan needs the name of the person who signed for it."
    And the server holds 0 scans for "APX-50000001"

  Scenario: A scan recorded after delivery is rejected
    Given the shipment was delivered 10 minutes ago
    And the courier saved an "InTransit" scan at "Back at depot" 5 minutes ago while offline
    When the device syncs
    Then the scan is marked failed on the device because "The shipment was closed as Delivered before this scan was recorded."
    And the server holds 0 scans for "APX-50000001"
