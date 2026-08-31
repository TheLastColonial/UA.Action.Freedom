Feature: Manifests API
    The deployed Freedom service exposes manifests at /manifests — the central document of
    the system, tying one vehicle on one convoy to its driver teams and its cargo.

    Every state change is its own POST, one per edge of docs/manifest-status.puml. Two rules
    the diagram cannot express are enforced here: a manifest may only be proposed against a
    convoy whose truck list is published, and once its Goods Movement Reference exists it can
    no longer be edited — the vehicle would otherwise reach a border carrying something HMRC
    was not told about.

    Approval is the fork in docs/process.puml: it confirms the manifest, freezes it, and hands
    the GMR to the customs worker. It is Administrator only, so the person who builds a
    manifest is not the person who signs it off.

    These scenarios run against the running containers (the edge on
    http://localhost:8080, Keycloak on http://localhost:8081) and skip themselves
    when that stack is not up.

Background:
    Given the Freedom API exposes "/manifests"

Scenario: Reading manifests requires a token
    When I GET "/manifests" without a token
    Then the response status is 401

Scenario: A ground officer is refused manifests
    Given I am authenticated as "groundofficer"
    When I GET "/manifests"
    Then the response status is 403

Scenario: A dispatcher opens a manifest in the Created state
    Given I am authenticated as "operator"
    And a manifest reference that is not yet used
    When I POST a manifest with no convoy
    Then the response status is 201
    When I GET the remembered manifest
    Then the response status is 200
    And the response body field "status" is "Created"

Scenario: Reusing a manifest reference is a conflict
    Given I am authenticated as "operator"
    And a manifest reference that is not yet used
    When I POST a manifest with no convoy
    Then the response status is 201
    When I POST a manifest with no convoy
    Then the response status is 409

Scenario: A manifest cannot be proposed until its convoy has published a truck list
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is not published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    When I POST "propose" on the remembered manifest
    Then the response status is 409
    When I GET the remembered manifest
    Then the response body field "status" is "Created"

Scenario: A manifest is proposed once its convoy has published a truck list
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    When I POST "propose" on the remembered manifest
    Then the response status is 204
    When I GET the remembered manifest
    Then the response body field "status" is "Proposed"

Scenario: A dispatcher may build a manifest but not approve it
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    When I POST "propose" on the remembered manifest
    Then the response status is 204
    When I POST "approve" on the remembered manifest
    Then the response status is 403

Scenario: An administrator approves a manifest, which freezes it
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    When I POST "propose" on the remembered manifest
    Then the response status is 204
    Given I am authenticated as "admin"
    When I POST "approve" on the remembered manifest
    Then the response status is 204
    When I GET the remembered manifest
    Then the response body field "status" is "Confirmed"
    And the response body field "frozen" is "True"

Scenario: A frozen manifest cannot be edited or deleted
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    When I POST "propose" on the remembered manifest
    Then the response status is 204
    Given I am authenticated as "admin"
    When I POST "approve" on the remembered manifest
    Then the response status is 204
    When I PUT the remembered manifest with body:
        """
        { "deliveryNotes": "changed after the GMR" }
        """
    Then the response status is 409
    When I DELETE the remembered manifest
    Then the response status is 409

Scenario: A frozen manifest still runs to delivery
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    When I POST "propose" on the remembered manifest
    Then the response status is 204
    Given I am authenticated as "admin"
    When I POST "approve" on the remembered manifest
    Then the response status is 204
    When I POST "prepare" on the remembered manifest
    Then the response status is 204
    When I POST "ready" on the remembered manifest
    Then the response status is 204
    When I POST "depart" on the remembered manifest
    Then the response status is 204
    When I POST "deliver" on the remembered manifest
    Then the response status is 204
    When I GET the remembered manifest
    Then the response body field "status" is "Delivered"

Scenario: A manifest cannot skip the preparation steps
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    When I POST "depart" on the remembered manifest
    Then the response status is 409

Scenario: A rejected manifest can be proposed again
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    When I POST "reject" on the remembered manifest
    Then the response status is 204
    When I POST "propose" on the remembered manifest
    Then the response status is 204
    When I GET the remembered manifest
    Then the response body field "status" is "Proposed"

Scenario: The border weight shows its fixed allowances
    Given I am authenticated as "operator"
    And a manifest reference that is not yet used
    When I POST a manifest with no convoy
    Then the response status is 201
    When I GET "/weight" on the remembered manifest
    Then the response status is 200
    And the response body field "crewAndBagsKg" is "200"
    And the response body field "fuelKg" is "45"
    And the response body field "unvalidatedBoxCount" is "0"

Scenario: Fetching an unknown manifest is a 404
    Given I am authenticated as "operator"
    When I GET "/manifests/NOSUCHMANIFEST"
    Then the response status is 404
