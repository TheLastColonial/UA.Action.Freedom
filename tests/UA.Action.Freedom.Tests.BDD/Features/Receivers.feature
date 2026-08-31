Feature: Receivers API
    The deployed Freedom service exposes receivers at /receivers, and their Ukrainian
    delivery detail at /receivers/{ref}/detail.

    The split is the point. /receivers carries an opaque reference, an organisation and a
    region — the shape that may be printed on something crossing a border. The delivery
    address and contact are Ground Officer only, and every resolve is audited. A manifest
    listing precise delivery addresses is a targeting document.

    Three separate controls enforce that and these scenarios exercise the outermost one:
    the receivers:detail policy. Behind it sit a distinct database identity and a DENY on
    the sensitive schema, both covered by the integration tests.

    These scenarios run against the running containers (the edge on
    http://localhost:8080, Keycloak on http://localhost:8081) and skip themselves
    when that stack is not up.

Background:
    Given the Freedom API exposes "/receivers"

Scenario: Reading receivers requires a token
    When I GET "/receivers" without a token
    Then the response status is 401

Scenario: A ground officer registers a receiving organisation
    Given I am authenticated as "groundofficer"
    When I POST "/receivers" with body:
        """
        { "organisation": "Kharkiv Regional Hospital", "region": "Kharkiv oblast" }
        """
    Then the response status is 201
    And the "Location" header names a new resource

Scenario: An operational role sees the region but never the address
    Given I am authenticated as "groundofficer"
    When I POST "/receivers" with body:
        """
        { "organisation": "Kharkiv Regional Hospital", "region": "Kharkiv oblast" }
        """
    Then the response status is 201
    When I PUT "/receivers/{id}/detail" with body:
        """
        { "contactName": "Olena Kovalenko", "contactPhone": "+380501234567", "addressLine1": "12 Vulytsia Sumska", "city": "Kharkiv", "postCode": "61002" }
        """
    Then the response status is 204
    Given I am authenticated as "operator"
    When I GET "/receivers/{id}"
    Then the response status is 200
    And the response body field "region" is "Kharkiv oblast"
    And the response body does not mention "Vulytsia Sumska"
    And the response body does not mention "Olena Kovalenko"
    And the response body does not mention "+380501234567"

Scenario: A dispatcher is refused a delivery address
    Given I am authenticated as "groundofficer"
    When I POST "/receivers" with body:
        """
        { "organisation": "Kharkiv Regional Hospital", "region": "Kharkiv oblast" }
        """
    Then the response status is 201
    Given I am authenticated as "operator"
    When I GET "/receivers/{id}/detail"
    Then the response status is 403

Scenario: An administrator is refused a delivery address too
    Given I am authenticated as "groundofficer"
    When I POST "/receivers" with body:
        """
        { "organisation": "Kharkiv Regional Hospital", "region": "Kharkiv oblast" }
        """
    Then the response status is 201
    Given I am authenticated as "admin"
    When I GET "/receivers/{id}/detail"
    Then the response status is 403

Scenario: A ground officer records and resolves a delivery address
    Given I am authenticated as "groundofficer"
    When I POST "/receivers" with body:
        """
        { "organisation": "Kharkiv Regional Hospital", "region": "Kharkiv oblast" }
        """
    Then the response status is 201
    When I PUT "/receivers/{id}/detail" with body:
        """
        { "contactName": "Olena Kovalenko", "contactPhone": "+380501234567", "addressLine1": "12 Vulytsia Sumska", "city": "Kharkiv", "postCode": "61002" }
        """
    Then the response status is 204
    When I GET "/receivers/{id}/detail?reason=Delivery%20scheduled%2012%20Sept"
    Then the response status is 200
    And the response body field "addressLine1" is "12 Vulytsia Sumska"
    And the response body field "city" is "Kharkiv"

Scenario: A dispatcher may not record a delivery address
    Given I am authenticated as "groundofficer"
    When I POST "/receivers" with body:
        """
        { "organisation": "Kharkiv Regional Hospital", "region": "Kharkiv oblast" }
        """
    Then the response status is 201
    Given I am authenticated as "operator"
    When I PUT "/receivers/{id}/detail" with body:
        """
        { "contactName": "Olena Kovalenko", "contactPhone": "+380501234567", "addressLine1": "12 Vulytsia Sumska", "city": "Kharkiv" }
        """
    Then the response status is 403

Scenario: A rejected delivery address is never echoed back
    Given I am authenticated as "groundofficer"
    When I POST "/receivers" with body:
        """
        { "organisation": "Kharkiv Regional Hospital", "region": "Kharkiv oblast" }
        """
    Then the response status is 201
    When I PUT "/receivers/{id}/detail" with body:
        """
        { "contactName": "", "contactPhone": "+380501234567", "addressLine1": "12 Vulytsia Sumska", "city": "Kharkiv" }
        """
    Then the response status is 400
    And the response body does not mention "Vulytsia Sumska"
    And the response body does not mention "+380501234567"

Scenario: A receiver with no delivery address recorded is a 404 for the ground officer
    Given I am authenticated as "groundofficer"
    When I POST "/receivers" with body:
        """
        { "organisation": "Kharkiv Regional Hospital", "region": "Kharkiv oblast" }
        """
    Then the response status is 201
    When I GET "/receivers/{id}/detail"
    Then the response status is 404

Scenario: A loader may not register a receiver
    Given I am authenticated as "operator"
    When I POST "/receivers" with body:
        """
        { "organisation": "Somewhere Else", "region": "Lviv oblast" }
        """
    Then the response status is 403

Scenario: Fetching an unknown receiver is a 404
    Given I am authenticated as "operator"
    When I GET "/receivers/b3f1c4d2-5a6e-4f70-8901-2c3d4e5f6a7b"
    Then the response status is 404
