# Copilot-assisted: first drafted by GitHub Copilot (COPILOT-LOG.md, prompt 2), then reviewed and changed by hand.
@api
Feature: Access control
  Only signed-in staff may see shipments, only couriers do delivery work, only administrators remove
  shipments, and a courier records scans only under their own courier id. Partner systems use their
  own key. Every refusal explains itself in the standard Problem Details format.

  Runs at API level, because these rules are enforced by the authentication and authorization
  middleware, which only an HTTP request goes through.

  Background:
    Given shipment "APX-70000001" has been registered

  Scenario Outline: Any signed-in member of staff can see a shipment
    Given I am signed in as "<role>"
    When I look up the shipment
    Then I see shipment "APX-70000001"

    Examples:
      | role    |
      | Admin   |
      | Courier |

  Scenario Outline: Without a valid sign-in, nobody can see a shipment
    Given <sign-in>
    When I look up the shipment
    Then I am refused with status 401
    And the refusal is explained as Problem Details

    Examples:
      | sign-in                 |
      | I am not signed in      |
      | my sign-in has expired  |

  Scenario: An administrator can remove a shipment that has not been picked up
    Given I am signed in as an administrator
    When I remove the shipment
    Then the shipment is removed

  Scenario: A courier cannot remove a shipment
    Given I am signed in as a courier
    When I remove the shipment
    Then I am refused with status 403
    And the refusal is explained as Problem Details
    And the shipment still exists

  Scenario Outline: Only couriers do delivery work
    Given I am signed in as an administrator
    When I try to <work> the shipment
    Then I am refused with status 403
    And the refusal is explained as Problem Details

    Examples:
      | work                  |
      | change the status of  |
      | upload a scan for     |

  Scenario: A courier can only upload scans under their own courier id
    Given I am signed in as a courier
    When I upload a scan recorded by courier "CR-2002"
    Then I am refused with status 403
    And the refusal is explained as Problem Details
    And no scan is stored

  Scenario Outline: A partner system needs the configured key
    Given a partner system that sends <key>
    When it looks up the shipment through the partner route
    Then I am refused with status 401
    And the refusal is explained as Problem Details

    Examples:
      | key                 |
      | no key              |
      | a key we never gave |

  Scenario: A partner system with the configured key sees the status but not the recipient
    Given a partner system that sends the configured key
    When it looks up the shipment through the partner route
    Then it sees the status of shipment "APX-70000001"
    And it does not see the recipient's details
